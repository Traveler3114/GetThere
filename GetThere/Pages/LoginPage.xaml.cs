#nullable enable
using GetThere.Helpers;
using GetThere.Services;
using GetThereShared.Dtos;

namespace GetThere.Pages;

public partial class LoginPage : ContentPage
{
    private readonly AuthService _authService;

    public LoginPage(AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
        SizeChanged += OnPageSizeChanged;
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        PageUtility.ApplyTicketsStyleResponsive(Width, LoginCard);
    }

    private void TogglePassword_Clicked(object? sender, TappedEventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        TogglePasswordBtn.Text = PasswordEntry.IsPassword ? "Show" : "Hide";
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
    {
        await Shell.Current.GoToAsync("registration");
    }

    private async void GuestButton_Clicked(object? sender, EventArgs e)
    {
        _authService.Logout();
        App.GoToApp();
    }
}
