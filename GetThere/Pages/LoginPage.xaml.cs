using GetThere.Services;
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

        if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
        {
            ShowError("Please enter a valid email address.");
            return;
        }

        if (password.Length < 8)
        {
            ShowError("Password must be at least 8 characters.");
            return;
        }

        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        LoginButton.IsEnabled = false;

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
                ShowError("Invalid email or password.");
            }
        }
        catch (Exception ex)
        {
            ShowError("Login failed. " + ex.Message);
        }
        finally
        {
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            LoginButton.IsEnabled = true;
        }
    }

    private async void RegisterButton_Clicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new RegistrationPage());
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}