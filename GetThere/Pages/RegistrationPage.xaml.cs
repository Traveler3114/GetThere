using GetThere.Services;
using GetThereShared.Models;
using Microsoft.Maui.Controls;
using System;

namespace GetThere.Pages;

public partial class RegistrationPage : ContentPage
{
    private readonly AuthService _authService;

    public RegistrationPage()
    {
        InitializeComponent();
        _authService = Application.Current!.Handler.MauiContext!.Services.GetRequiredService<AuthService>();
    }

    private void ShowPasswordCheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        bool show = e.Value;
        PasswordEntry.IsPassword = !show;
        ConfirmPasswordEntry.IsPassword = !show;
    }

    private async void RegisterButton_Clicked(object sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;

        string fullName = FullNameEntry.Text?.Trim() ?? string.Empty;
        string email = EmailEntry.Text?.Trim() ?? string.Empty;
        string password = PasswordEntry.Text ?? string.Empty;
        string confirm = ConfirmPasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(fullName))
        {
            ShowError("Please enter your full name.");
            return;
        }

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

        if (password != confirm)
        {
            ShowError("Passwords do not match.");
            return;
        }

        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        RegisterButton.IsEnabled = false;

        try
        {
            string username = email.Split('@')[0];

            var (success, message) = await _authService.RegisterAsync(
                username, email, password, fullName);

            if (success)
            {
                await DisplayAlert("Success", "Account created successfully.", "OK");
                await Navigation.PushAsync(new LoginPage());
            }
            else
            {
                ShowError("Registration failed. " + message);
            }
        }
        catch (Exception ex)
        {
            ShowError("Registration failed. " + ex.Message);
        }
        finally
        {
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            RegisterButton.IsEnabled = true;
        }
    }

    private async void LoginButton_Clicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new LoginPage());
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