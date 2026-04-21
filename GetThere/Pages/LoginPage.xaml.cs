using System;
using GetThere.Helpers;
using GetThere.Services;
using GetThereShared.Dtos;

namespace GetThere.Pages;

public partial class LoginPage : ContentPage
{
    private readonly AuthService _authService;
    private CancellationTokenSource? _loadingCts;

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

        StartLoadingAnimations();

        try
        {
            var result = await _authService.LoginAsync(loginData, RememberMeCheckbox.IsChecked);

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
            StopLoadingAnimations();
        }
    }

    private async void StartLoadingAnimations()
    {
        LoginButton.IsVisible = false;
        PremiumLoadingState.IsVisible = true;
        _loadingCts = new CancellationTokenSource();
        var token = _loadingCts.Token;

        // 1. Shimmer sweep
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                MainThread.BeginInvokeOnMainThread(() => ShimmerBox.TranslationX = -70);
                await ShimmerBox.TranslateTo(140, 0, 1000, Easing.Linear);
                await Task.Delay(200, token);
            }
        }, token);

    }

    private void StopLoadingAnimations()
    {
        _loadingCts?.Cancel();
        PremiumLoadingState.IsVisible = false;
        LoginButton.IsVisible = true;
    }

    private async void RegisterButton_Clicked(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("registration");
    }

    private void RememberMeLabel_Tapped(object? sender, TappedEventArgs e)
    {
        RememberMeCheckbox.IsChecked = !RememberMeCheckbox.IsChecked;
    }

    private async void GuestButton_Clicked(object? sender, EventArgs e)
    {
        await _authService.Logout();
        App.GoToApp();
    }
}
