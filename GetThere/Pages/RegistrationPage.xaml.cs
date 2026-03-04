using GetThere.Helpers;
using GetThere.Services;
using GetThereShared.Dtos;

namespace GetThere.Pages;

public partial class RegistrationPage : ContentPage
{
    private readonly AuthService _authService;
    private bool _passwordVisible = false;
    private bool _confirmPasswordVisible = false;

    public RegistrationPage(AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    private void TogglePassword_Clicked(object? sender, EventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        PasswordEntry.IsPassword = !_passwordVisible;
        TogglePasswordBtn.Text = _passwordVisible ? "🙈" : "👁";
    }

    private void ToggleConfirmPassword_Clicked(object? sender, EventArgs e)
    {
        _confirmPasswordVisible = !_confirmPasswordVisible;
        ConfirmPasswordEntry.IsPassword = !_confirmPasswordVisible;
        ToggleConfirmPasswordBtn.Text = _confirmPasswordVisible ? "🙈" : "👁";
    }

    private async void RegisterButton_Clicked(object? sender, EventArgs e)
    {
        PageUtility.HideError(ErrorLabel);

        var rdto = new RegisterDto
        {
            FullName = FullNameEntry.Text?.Trim() ?? string.Empty,
            Email = EmailEntry.Text?.Trim() ?? string.Empty,
            Password = PasswordEntry.Text ?? string.Empty,
        };

        string confirm = ConfirmPasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rdto.FullName))
        {
            PageUtility.ShowError(ErrorLabel, "Please enter your full name.");
            return;
        }

        if (string.IsNullOrWhiteSpace(rdto.Email) || !PageUtility.IsValidEmail(rdto.Email))
        {
            PageUtility.ShowError(ErrorLabel, "Please enter a valid email address.");
            return;
        }

        if (rdto.Password.Length < 8)
        {
            PageUtility.ShowError(ErrorLabel, "Password must be at least 8 characters.");
            return;
        }

        if (rdto.Password != confirm)
        {
            PageUtility.ShowError(ErrorLabel, "Passwords do not match.");
            return;
        }

        PageUtility.SetBusy(BusyIndicator, RegisterButton, true);

        try
        {
            var result = await _authService.RegisterAsync(rdto);

            if (result.Success)
            {
                await DisplayAlertAsync("Account created", "You can now log in.", "Continue");
                await Shell.Current.GoToAsync("//login");
            }
            else
            {
                PageUtility.ShowError(ErrorLabel, result.Message ?? "Registration failed.");
            }
        }
        catch (Exception ex)
        {
            PageUtility.ShowError(ErrorLabel, "Registration failed. " + ex.Message);
        }
        finally
        {
            PageUtility.SetBusy(BusyIndicator, RegisterButton, false);
        }
    }

    private async void LoginButton_Clicked(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("//login");
}