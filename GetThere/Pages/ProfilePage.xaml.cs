#nullable enable
using GetThere.Helpers;
using GetThere.Services;
using GetThere.Components;
using GetThereShared.Dtos;

namespace GetThere.Pages;

public partial class ProfilePage : ContentPage
{
    private readonly WalletService _walletService;
    private readonly PaymentService _paymentService;
    private readonly AuthService _authService;

    public ProfilePage(WalletService walletService, PaymentService paymentService, AuthService authService)
    {
        InitializeComponent();
        _walletService = walletService;
        _paymentService = paymentService;
        _authService = authService;
        SizeChanged += OnPageSizeChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateResponsiveLayout();
        await LoadProfileAsync();
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        if (Width <= 0)
            return;

        var isMobile = Width < PageUtility.MobileBreakpoint;

        if (isMobile)
        {
            var mobileWidth = Math.Min(Math.Max(Width * 0.84, 300), 420);
            MainCard.WidthRequest = mobileWidth;
            MainCard.HeightRequest = 180;
            MainCard.HorizontalOptions = LayoutOptions.Center;

            HistoryContainer.WidthRequest = mobileWidth;
            HistoryContainer.HorizontalOptions = LayoutOptions.Center;

            TopUpButton.Margin = new Thickness(0, -34, 0, 0);
            return;
        }

        PageUtility.ApplyTicketsStyleResponsive(Width, MainCard);
        PageUtility.ApplyTicketsStyleResponsive(Width, HistoryContainer);
        MainCard.HeightRequest = 210;
        TopUpButton.Margin = new Thickness(0, -48, 0, 0);
    }

    private async Task LoadProfileAsync()
    {
        try
        {
            var fullName = await _authService.GetFullNameAsync();
            var email = await _authService.GetEmailAsync();

            if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(email))
            {
                NameLabelOnCard.Text = "Guest";
                GuestOverlay.IsVisible = true;
                return;
            }

            GuestOverlay.IsVisible = false;
            NameLabelOnCard.Text = fullName ?? "User";

            await LoadWalletAsync();
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", "Could not load profile: " + ex.Message, "OK");
        }
    }

    private async Task LoadWalletAsync()
    {
        var result = await _walletService.GetWalletAsync();
        if (result.Success && result.Data != null)
        {
            BalanceLabelOnCard.Text = $"€{result.Data.Balance:F2}";
        }
    }

    private async Task LoadHistoryAsync()
    {
        BusyLoader.IsVisible = true;
        BusyLoader.IsRunning = true;
        NoItemsLabel.IsVisible = false;

        try
        {
            var result = await _walletService.GetTransactionsAsync();
            if (result.Success && result.Data != null)
            {
                var list = result.Data.OrderByDescending(t => t.Timestamp).ToList();
                MainCollection.ItemsSource = list;
                NoItemsLabel.IsVisible = !list.Any();
            }
        }
        catch
        {
            NoItemsLabel.IsVisible = true;
        }
        finally
        {
            BusyLoader.IsVisible = false;
            BusyLoader.IsRunning = false;
        }
    }

    private void OnLoginRegisterClicked(object? sender, EventArgs e)
    {
        App.GoToLogin();
    }


    private async void OnTopUpClicked(object? sender, EventArgs e)
    {
        var input = await DisplayPromptAsync("Top Up", "Amount (€):", "Next", "Cancel", "10.00", -1, Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(input)) return;

        if (decimal.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount) && amount > 0)
        {
            try
            {
                var provResult = await _paymentService.GetProvidersAsync();
                if (provResult.Success && provResult.Data != null && provResult.Data.Any())
                {
                    var providers = provResult.Data.ToList();
                    var chosen = await DisplayActionSheetAsync("Provider", "Cancel", null, providers.Select(p => p.Name).ToArray());
                    if (chosen != null && chosen != "Cancel")
                    {
                        var provider = providers.First(p => p.Name == chosen);
                        var success = await _paymentService.TopUpAsync(new TopUpDto { Amount = amount, PaymentProviderId = provider.Id });
                        if (success.Success)
                        {
                            await LoadWalletAsync();
                            await DisplayAlertAsync("Success", $"€{amount:F2} added!", "OK");
                            await LoadHistoryAsync();
                        }
                    }
                }
            }
            catch (Exception ex) { await DisplayAlertAsync("Error", ex.Message, "OK"); }
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        if (await DisplayAlertAsync("Logout", "Are you sure?", "Yes", "No"))
        {
            _authService.Logout();
            App.GoToLogin();
        }
    }
}
