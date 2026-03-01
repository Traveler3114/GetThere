using GetThere.Services;
using GetThere.Helpers;
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
            PageUtility.ShowError(ErrorLabel, "Please enter your full name.");
            return;
        }

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

        if (password != confirm)
        {
            PageUtility.ShowError(ErrorLabel, "Passwords do not match.");
            return;
        }

        PageUtility.SetBusy(BusyIndicator, RegisterButton, true);

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
                PageUtility.ShowError(ErrorLabel, "Registration failed. " + message);
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

    private async void LoginButton_Clicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new LoginPage());
    }
}