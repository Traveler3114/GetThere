using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Helpers;
using GetThere.Localization;
using GetThere.Services;
using GetThereShared.Contracts;

namespace GetThere.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly AuthService _authService;
    private readonly ImportSyncService _importSync;
    private readonly IAnalyticsService _analytics;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _rememberMe;

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private string _passwordToggleText = LocalizationService.Instance["Login_ShowPassword"];

    public LoginViewModel(AuthService authService, ImportSyncService importSync, IAnalyticsService analytics)
    {
        _authService = authService;
        _importSync = importSync;
        _analytics = analytics;
    }

    partial void OnIsPasswordVisibleChanged(bool value)
    {
        PasswordToggleText = value
            ? LocalizationService.Instance["Login_HidePassword"]
            : LocalizationService.Instance["Login_ShowPassword"];
    }

    [RelayCommand]
    private void TogglePassword()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }

    [RelayCommand]
    private async Task Login()
    {
        HasError = false;

        if (string.IsNullOrWhiteSpace(Email) || !PageUtility.IsValidEmail(Email))
        {
            ErrorText = LocalizationService.Instance["Login_InvalidEmail"];
            HasError = true;
            return;
        }

        IsLoading = true;

        try
        {
            var loginData = new LoginRequest(Email.Trim(), Password);
            var result = await _authService.LoginAsync(loginData, RememberMe);

            if (result.Success)
            {
                _analytics.TrackEvent("login_success", new() { ["method"] = RememberMe ? "remember" : "standard" });

                // The guest-to-account upgrade. Anything imported before signing in is pushed now,
                // so the wallet the user lands on already contains it. Nothing did this before —
                // ClearGuest ran on logout and never on login, and no data moved, so a guest who
                // imported tickets and then signed up found an empty wallet.
                //
                // Best-effort on purpose: a failed push leaves the queue intact for the next attempt
                // and must not block a sign-in that otherwise worked.
                var pushed = await _importSync.FlushAsync();
                if (pushed > 0)
                    _analytics.TrackEvent("guest_imports_claimed", new() { ["count"] = pushed.ToString() });

                App.GoToApp();
            }
            else
            {
                ErrorText = ApiMessageMapper.Localize(result.Code, result.Message)
                    ?? LocalizationService.Instance["Login_Failed"];
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            ErrorText = LocalizationService.Instance["Login_Failed"] + " " + ex.Message;
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task GoToRegister()
    {
        await Shell.Current.GoToAsync("registration");
    }

    [RelayCommand]
    private void ToggleRememberMe()
    {
        RememberMe = !RememberMe;
    }

    [RelayCommand]
    private void ContinueAsGuest()
    {
        _analytics.TrackEvent("continue_as_guest");
        AuthService.SetGuest();
        App.GoToApp();
    }
}
