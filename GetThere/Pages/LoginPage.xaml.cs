using GetThere.Services;
using GetThere.Helpers;
using Microsoft.Maui.Controls;
using System;
using GetThereShared.Dtos;

namespace GetThere.Pages;

public partial class LoginPage : ContentPage
{
    private readonly AuthService _authService;

    public LoginPage(AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    private void ShowPasswordCheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        PasswordEntry.IsPassword = !e.Value;
    }

    private async void LoginButton_Clicked(object sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;

        LoginDto loginData = new LoginDto
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
            {
                await DisplayAlertAsync("Success", "Welcome back!", "OK");
                // TODO: Navigate to main app page
            }
            else
            {
                PageUtility.ShowError(ErrorLabel, "Login failed. " + result.Message);
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
        //await Navigation.PushAsync(new RegistrationPage());
        await Shell.Current.GoToAsync("registration");
    }


    private async void MapButtonClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("mainpage");
    }
}