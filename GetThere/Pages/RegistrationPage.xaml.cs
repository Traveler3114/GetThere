using GetThere.Services;
using GetThere.Helpers;
using GetThereShared.Models;
using Microsoft.Maui.Controls;
using System;
using GetThereAPI.Models;

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

        RegisterDto rdto = new RegisterDto
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

            var (success, message) = await _authService.RegisterAsync(rdto);

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