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

    public LoginViewModel(AuthService authService)
    {
        _authService = authService;
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
                App.GoToApp();
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
        AuthService.SetGuest();
        App.GoToApp();
    }
}
