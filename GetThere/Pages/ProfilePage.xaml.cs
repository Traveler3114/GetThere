#nullable enable
using GetThere.Helpers;
using GetThere.Services;
using GetThere.Components;
using GetThereShared.Dtos;
using GetThereShared.Enums;

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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadProfileAsync();
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

<<<<<<< HEAD
=======
    private void OnLoginRegisterClicked(object? sender, EventArgs e)
    {
        App.GoToLogin();
    }

    private void ApplyTicketFilter(TicketStatus filter)
    {
        _currentFilter = filter;
        var filtered = _allTickets.Where(t => t.Status == filter).ToList();
        
        MainCollection.ItemTemplate = (DataTemplate)Resources["TicketTemplate"];
        MainCollection.ItemsSource = filtered;
        NoItemsLabel.IsVisible = !filtered.Any();

        CurrentFilterLabel.Text = $"{filter} Tickets";
    }

    private async void OnShowFilterOptions(object? sender, EventArgs e)
    {
        FilterBottomSheet.IsVisible = true;
        await Task.WhenAll(
            FilterBottomSheet.FadeToAsync(1, 200),
            FilterContent.TranslateToAsync(0, 0, 300, Easing.CubicOut)
        );
    }

    private async void OnHideFilterBottomSheet(object? sender, EventArgs e)
    {
        await Task.WhenAll(
            FilterBottomSheet.FadeToAsync(0, 200),
            FilterContent.TranslateToAsync(0, 600, 300, Easing.CubicIn)
        );
        FilterBottomSheet.IsVisible = false;
    }

    private async void OnFilterOptionClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string chosen)
        {
            if (Enum.TryParse<TicketStatus>(chosen, out var status))
            {
                ApplyTicketFilter(status);
            }
        }
        OnHideFilterBottomSheet(sender, e);
    }

    private async void TicketsTab_Clicked(object? sender, EventArgs e)
    {
        _isShowingTickets = true;
        FilterRow.IsVisible = true;
        
        bool isDark = Application.Current!.RequestedTheme == AppTheme.Dark;
        
        TicketsTabBtn.Background = null;
        TicketsTabBtn.BackgroundColor = Color.FromArgb(isDark ? "#2C2C2E" : "#EBEBEC");
        TicketsTabBtn.TextColor = isDark ? Colors.White : Colors.Black;
        
        HistoryTabBtn.Background = null;
        HistoryTabBtn.BackgroundColor = Colors.Transparent;
        HistoryTabBtn.TextColor = isDark ? Color.FromArgb("#AAAAAA") : Colors.Gray;

        await LoadTicketsAsync();
    }

    private async void HistoryTab_Clicked(object? sender, EventArgs e)
    {
        _isShowingTickets = false;
        FilterRow.IsVisible = false;
        
        bool isDark = Application.Current!.RequestedTheme == AppTheme.Dark;
        
        HistoryTabBtn.Background = null;
        HistoryTabBtn.BackgroundColor = Color.FromArgb(isDark ? "#2C2C2E" : "#EBEBEC");
        HistoryTabBtn.TextColor = isDark ? Colors.White : Colors.Black;
        
        TicketsTabBtn.Background = null;
        TicketsTabBtn.BackgroundColor = Colors.Transparent;
        TicketsTabBtn.TextColor = isDark ? Color.FromArgb("#AAAAAA") : Colors.Gray;

        await LoadHistoryAsync();
    }

>>>>>>> main
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