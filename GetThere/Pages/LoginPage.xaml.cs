using GetThere.Services;
using GetThere.Helpers;
using Microsoft.Maui.Controls;
using System;

namespace GetThere.Pages;

public partial class LoginPage : ContentPage
{
    private readonly AuthService _authService;

    public LoginPage()
    {
        InitializeComponent();
        _authService = Application.Current!.Handler.MauiContext!.Services.GetRequiredService<AuthService>();
    }

    private void ShowPasswordCheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        PasswordEntry.IsPassword = !e.Value;
    }

    private async void LoginButton_Clicked(object sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;

        string email = EmailEntry.Text?.Trim() ?? string.Empty;
        string password = PasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || !PageUtility.IsValidEmail(email))
        {
            PageUtility.ShowError(ErrorLabel, "Please enter a valid email address.");
            return;
        }

        if (password.Length < 8)
        {
            PageUtility.ShowError(ErrorLabel, "Password must be at least 8 characters.");
            return;
        }

        PageUtility.SetBusy(BusyIndicator, LoginButton, true);

        try
        {
            var user = await _authService.LoginAsync(email, password);

            if (user != null)
            {
                await DisplayAlert("Success", $"Welcome back, {user.FullName ?? user.Username}!", "OK");
                // TODO: Navigate to main app page
            }
            else
            {
                PageUtility.ShowError(ErrorLabel, "Invalid email or password.");
            }
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

    private async void RegisterButton_Clicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new RegistrationPage());
    }
}