using GetThere.Services;
using GetThereShared.Dtos;
using System.Text.Json;
using System.Text;
using GetThere.Helpers;

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
        PageUtility.SetBusy(BusyIndicator, TopUpButton, true);
        ErrorLabel.IsVisible = false;

        try
        {
            // Read JWT from SecureStorage and decode the payload (no library needed)
            var token = await _authService.GetTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                PageUtility.ShowError(ErrorLabel, "Not logged in.");
                return;
            }

            // JWT is 3 base64 parts: header.payload.signature
            // We only need the payload (index 1) — no verification needed client-side
            var payload = token.Split('.')[1];

            // Base64url -> Base64 padding fix
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            var fullName = claims?.GetValueOrDefault("given_name").GetString() ?? "User";
            var email = claims?.GetValueOrDefault("email").GetString() ?? string.Empty;

            NameLabel.Text = fullName;
            EmailLabel.Text = email;
            AvatarLabel.Text = string.IsNullOrWhiteSpace(fullName) ? "?" : fullName[0].ToString().ToUpper();

            // Fetch wallet — JWT auto-attached by AuthenticatedHttpHandler
            var walletResult = await _walletService.GetWalletAsync();
            if (walletResult.Success && walletResult.Data != null)
                BalanceLabel.Text = $"€ {walletResult.Data.Balance:F2}";
            else
                PageUtility.ShowError(ErrorLabel, walletResult.Message ?? "Failed to load wallet.");
        }
        catch (Exception ex)
        {
            PageUtility.ShowError(ErrorLabel, "Error loading profile: " + ex.Message);
        }
        finally
        {
            PageUtility.SetBusy(BusyIndicator, TopUpButton, false);
        }
    }

    private async void TopUpButton_Clicked(object sender, EventArgs e)
    {
        var input = await DisplayPromptAsync(
            "Top Up Wallet",
            "Enter amount to add (€):",
            accept: "Top Up",
            cancel: "Cancel",
            placeholder: "e.g. 10.00",
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(input))
            return;

        if (!decimal.TryParse(input, out var amount) || amount <= 0)
        {
            await DisplayAlert("Invalid Amount", "Please enter a valid positive number.", "OK");
            return;
        }

        PageUtility.SetBusy(BusyIndicator, TopUpButton, true);
        ErrorLabel.IsVisible = false;

        try
        {
            var dto = new TopUpDto { Amount = amount };
            var result = await _paymentService.TopUpAsync(dto);

            if (result.Success && result.Data != null)
            {
                BalanceLabel.Text = $"€ {result.Data.Balance:F2}";
                await DisplayAlert("Success", $"€{amount:F2} added to your wallet!", "OK");
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
            PageUtility.SetBusy(BusyIndicator, TopUpButton, false);
        }
    }
}