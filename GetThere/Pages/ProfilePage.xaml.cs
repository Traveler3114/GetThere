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
    private readonly TicketService _ticketService;
    private readonly PaymentService _paymentService;
    private readonly AuthService _authService;

    private IEnumerable<TicketDto> _allTickets = [];
    private TicketStatus _currentFilter = TicketStatus.Active;

    public ProfilePage(WalletService walletService, TicketService ticketService,
                       PaymentService paymentService, AuthService authService)
    {
        InitializeComponent();
        _walletService = walletService;
        _ticketService = ticketService;
        _paymentService = paymentService;
        _authService = authService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadProfileAsync();
    }

    // ── Profile header ─────────────────────────────────────────────────────

    private async Task LoadProfileAsync()
    {
        WalletBusy.IsVisible = true;
        WalletBusy.IsRunning = true;
        ErrorLabel.IsVisible = false;

        try
        {
            var fullName = await _authService.GetFullNameAsync();
            var email = await _authService.GetEmailAsync();

            if (fullName == null && email == null)
            {
                // User is not logged in (guest mode)
                LoginRequiredOverlay.IsVisible = true;
                NameLabel.Text = "Guest";
                EmailLabel.Text = "Not logged in";
                AvatarLabel.Text = "?";
                WalletBusy.IsVisible = false;
                WalletBusy.IsRunning = false;
                ErrorLabel.IsVisible = false;
                TicketsBusy.IsVisible = false;
                TicketsBusy.IsRunning = false;
                return;
            }

            // User is logged in
            LoginRequiredOverlay.IsVisible = false;
            NameLabel.Text = fullName ?? "User";
            EmailLabel.Text = email ?? string.Empty;
            AvatarLabel.Text = string.IsNullOrWhiteSpace(fullName)
                ? "?"
                : fullName[0].ToString().ToUpper();

            // Notify shell to potentially update its icon (e.g. if we had a picture)
            if (Shell.Current is AppShell appShell)
            {
                // We don't have a picture URL yet, but if we did:
                // appShell.UpdateProfileIcon(ImageSource.FromUri(new Uri(pictureUrl)));
                appShell.UpdateProfileIcon(null); // Resets to default icon_profile.svg
            }

            await Task.WhenAll(LoadWalletAsync(), LoadTicketsAsync());
        }
        catch (Exception ex)
        {
            PageUtility.ShowError(ErrorLabel, "Error loading profile: " + ex.Message);
        }
        finally
        {
            WalletBusy.IsVisible = false;
            WalletBusy.IsRunning = false;
        }
    }

    // ── Wallet ─────────────────────────────────────────────────────────────

    private async Task LoadWalletAsync()
    {
        var result = await _walletService.GetWalletAsync();
        if (result.Success && result.Data != null)
        {
            BalanceLabel.Text = $"€ {result.Data.Balance:F2}";
            LastUpdatedLabel.Text = $"Updated {result.Data.LastUpdated:dd MMM yyyy, HH:mm}";
        }
        else
        {
            PageUtility.ShowError(ErrorLabel, result.Message ?? "Failed to load wallet.");
        }
    }

    // ── Tickets ────────────────────────────────────────────────────────────

    private async Task LoadTicketsAsync()
    {
        TicketsBusy.IsVisible = true;
        TicketsBusy.IsRunning = true;
        NoTicketsLabel.IsVisible = false;

        try
        {
            var result = await _ticketService.GetTicketsAsync();
            if (result.Success && result.Data != null)
            {
                _allTickets = result.Data;
                ApplyTicketFilter(_currentFilter);
            }
            else
            {
                _allTickets = [];
                NoTicketsLabel.IsVisible = true;
            }
        }
        catch
        {
            _allTickets = [];
            NoTicketsLabel.IsVisible = true;
        }
        finally
        {
            TicketsBusy.IsVisible = false;
            TicketsBusy.IsRunning = false;
        }
    }

    private void ApplyTicketFilter(TicketStatus filter)
    {
        _currentFilter = filter;
        var filtered = _allTickets.Where(t => t.Status == filter).ToList();
        TicketsCollection.ItemsSource = filtered;
        NoTicketsLabel.IsVisible = filtered.Count == 0;

        ResetFilterChips();
        SetChipActive(filter switch
        {
            TicketStatus.Active => ActiveBtn,
            TicketStatus.Expired => ExpiredBtn,
            TicketStatus.Used => UsedBtn,
            _ => ActiveBtn
        });
    }

    private void ResetFilterChips()
    {
        var isDark = Application.Current!.RequestedTheme == AppTheme.Dark;
        var inactiveColor = isDark ? Color.FromArgb("#2C2C2E") : Color.FromArgb("#E0E0E0");
        var inactiveText = isDark ? Colors.White : Colors.Black;

        foreach (var btn in new[] { ActiveBtn, ExpiredBtn, UsedBtn })
        {
            btn.BackgroundColor = inactiveColor;
            btn.TextColor = inactiveText;
        }
    }

    private static void SetChipActive(Button btn)
    {
        btn.BackgroundColor = Color.FromArgb("#4CAF50");
        btn.TextColor = Colors.White;
    }

    // ── History ────────────────────────────────────────────────────────────

    private async Task LoadHistoryAsync()
    {
        HistoryBusy.IsVisible = true;
        HistoryBusy.IsRunning = true;
        NoHistoryLabel.IsVisible = false;

        try
        {
            var result = await _walletService.GetTransactionsAsync();
            if (result.Success && result.Data != null)
            {
                var list = result.Data.ToList();
                HistoryCollection.ItemsSource = list;
                NoHistoryLabel.IsVisible = list.Count == 0;
            }
            else
            {
                NoHistoryLabel.IsVisible = true;
            }
        }
        catch
        {
            NoHistoryLabel.IsVisible = true;
        }
        finally
        {
            HistoryBusy.IsVisible = false;
            HistoryBusy.IsRunning = false;
        }
    }

    // ── Segment tab switchers ──────────────────────────────────────────────

    private void TicketsTab_Clicked(object? sender, EventArgs e)
    {
        TicketsView.IsVisible = true;
        HistoryView.IsVisible = false;
        FilterChipsRow.IsVisible = true;

        TicketsTabBtn.BackgroundColor = Color.FromArgb("#4CAF50");
        TicketsTabBtn.TextColor = Colors.White;

        var isDark = Application.Current!.RequestedTheme == AppTheme.Dark;
        var inactiveColor = isDark ? Color.FromArgb("#2C2C2E") : Color.FromArgb("#E0E0E0");
        var inactiveText = isDark ? Colors.White : Colors.Black;

        HistoryTabBtn.BackgroundColor = inactiveColor;
        HistoryTabBtn.TextColor = inactiveText;
    }

    private async void HistoryTab_Clicked(object? sender, EventArgs e)
    {
        TicketsView.IsVisible = false;
        HistoryView.IsVisible = true;
        FilterChipsRow.IsVisible = false;

        HistoryTabBtn.BackgroundColor = Color.FromArgb("#4CAF50");
        HistoryTabBtn.TextColor = Colors.White;

        var isDark = Application.Current!.RequestedTheme == AppTheme.Dark;
        var inactiveColor = isDark ? Color.FromArgb("#2C2C2E") : Color.FromArgb("#E0E0E0");
        var inactiveText = isDark ? Colors.White : Colors.Black;

        TicketsTabBtn.BackgroundColor = inactiveColor;
        TicketsTabBtn.TextColor = inactiveText;

        await LoadHistoryAsync();
    }

    private void ActiveFilter_Clicked(object? sender, EventArgs e)
        => ApplyTicketFilter(TicketStatus.Active);

    private void ExpiredFilter_Clicked(object? sender, EventArgs e)
        => ApplyTicketFilter(TicketStatus.Expired);

    private void UsedFilter_Clicked(object? sender, EventArgs e)
        => ApplyTicketFilter(TicketStatus.Used);

    private async void TopUpButton_Clicked(object? sender, EventArgs e)
    {
        var input = await DisplayPromptAsync(
            "Top Up Wallet",
            "Enter amount to add (€):",
            accept: "Next",
            cancel: "Cancel",
            placeholder: "e.g. 10.00",
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(input))
            return;

        input = input.Replace(',', '.');
        if (!decimal.TryParse(input,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var amount) || amount <= 0)
        {
            await DisplayAlertAsync("Invalid Amount", "Please enter a valid positive number.", "OK");
            return;
        }

        PageUtility.SetBusy(WalletBusy, TopUpButton, true);
        ErrorLabel.IsVisible = false;

        List<PaymentProviderDto> providers;
        try
        {
            var result = await _paymentService.GetProvidersAsync();
            if (!result.Success || result.Data == null || !result.Data.Any())
            {
                PageUtility.ShowError(ErrorLabel, "No payment providers available.");
                return;
            }
            providers = result.Data.ToList();
        }
        catch (Exception ex)
        {
            PageUtility.ShowError(ErrorLabel, "Could not load payment providers: " + ex.Message);
            return;
        }
        finally
        {
            PageUtility.SetBusy(WalletBusy, TopUpButton, false);
        }

        var converter = new ProviderIconConverter();

        var providerNames = providers
            .Select(p => $"{converter.Convert(p.Name, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)}  {p.Name}")
            .ToArray();

        var chosen = await DisplayActionSheetAsync(
            $"Pay €{amount:F2} with",
            "Cancel",
            null,
            providerNames);

        if (string.IsNullOrEmpty(chosen) || chosen == "Cancel")
            return;

        var selectedProvider = providers[Array.IndexOf(providerNames, chosen)];

        var confirmed = await DisplayAlertAsync(
            "Confirm Top Up",
            $"Add €{amount:F2} to your wallet via {selectedProvider.Name}?",
            "Confirm",
            "Cancel");

        if (!confirmed)
            return;

        PageUtility.SetBusy(WalletBusy, TopUpButton, true);
        ErrorLabel.IsVisible = false;

        try
        {
            var dto = new TopUpDto
            {
                Amount = amount,
                PaymentProviderId = selectedProvider.Id
            };

            var result = await _paymentService.TopUpAsync(dto);

            if (result.Success && result.Data != null)
            {
                BalanceLabel.Text = $"€ {result.Data.Balance:F2}";
                await DisplayAlertAsync("✅ Success", $"€{amount:F2} added via {selectedProvider.Name}!", "OK");
            }
            else
            {
                PageUtility.ShowError(ErrorLabel, result.Message ?? "Top-up failed.");
            }
        }
        catch (Exception ex)
        {
            PageUtility.ShowError(ErrorLabel, "Top-up error: " + ex.Message);
        }
        finally
        {
            PageUtility.SetBusy(WalletBusy, TopUpButton, false);
        }
    }

    private async void RefreshBalance_Clicked(object? sender, EventArgs e)
    {
        RefreshBusy.IsVisible = true;
        RefreshBusy.IsRunning = true;
        await LoadWalletAsync();
        RefreshBusy.IsVisible = false;
        RefreshBusy.IsRunning = false;
    }

    private void OnPanUpdate(object? sender, PanUpdatedEventArgs e)
    {
        // Using FindByName as a workaround for cases where the auto-generated field 'AnimatedBg' 
        // is not recognized by the compiler/IDE in the partial class.
        var animatedBg = this.FindByName<AnimatedBackground>("AnimatedBg");
        if (animatedBg != null)
        {
            animatedBg.XOffset = (float)e.TotalX;
            animatedBg.YOffset = (float)e.TotalY;
        }
    }

    private void GoBackToLogInButton_Clicked(object? sender, EventArgs e)
    {
        App.GoToLogin();
    }
}