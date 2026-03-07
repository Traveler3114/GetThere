using GetThere.Helpers;
using GetThere.Services;
using GetThereShared.Dtos;

namespace GetThere.Pages;

public partial class LoginPage : ContentPage
{
    private readonly AuthService _authService;
    private bool _passwordVisible = false;

    public LoginPage(AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    private void TogglePassword_Clicked(object? sender, EventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        PasswordEntry.IsPassword = !_passwordVisible;
        TogglePasswordBtn.Text = _passwordVisible ? "Hide" : "Show";
    }

    private async void LoginButton_Clicked(object? sender, EventArgs e)
    {
        PageUtility.HideError(ErrorLabel);

        var loginData = new LoginDto
        {
            Email = EmailEntry.Text?.Trim() ?? string.Empty,
            Password = PasswordEntry.Text ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(loginData.Email) || !PageUtility.IsValidEmail(loginData.Email))
        {
            PageUtility.ShowError(ErrorLabel, "Please enter a valid email address.");
            return;
        }

        if (loginData.Password.Length < 8)
        {
            PageUtility.ShowError(ErrorLabel, "Password must be at least 8 characters.");
            return;
        }

        PageUtility.SetBusy(BusyIndicator, LoginButton, true);

        try
        {
            var result = await _authService.LoginAsync(loginData);

            if (result.Success)
                App.GoToApp();
            else
                PageUtility.ShowError(ErrorLabel, result.Message ?? "Login failed.");
        }
        catch (Exception ex)
        {
            PageUtility.ShowError(ErrorLabel, "Login failed. " + ex.Message);
        }
        finally
        {
            PageUtility.SetBusy(BusyIndicator, LoginButton, false);
        }
    }

    private async void RegisterButton_Clicked(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("///registration");

    private async void GuestButton_Clicked(object? sender, EventArgs e)
        => App.GoToApp();

    private void OnPanUpdate(object sender, PanUpdatedEventArgs e)
    {
        AnimatedBg.XOffset = (float)e.TotalX;
        AnimatedBg.YOffset = (float)e.TotalY;
    }
}