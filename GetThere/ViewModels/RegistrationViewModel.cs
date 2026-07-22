using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Helpers;
using GetThere.Localization;
using GetThere.Services;
using GetThereShared.Contracts;

namespace GetThere.ViewModels;

public partial class RegistrationViewModel : BaseViewModel
{
    private readonly AuthService _authService;

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private string _passwordToggleText = LocalizationService.Instance["Login_ShowPassword"];

    [ObservableProperty]
    private bool _isConfirmPasswordVisible;

    [ObservableProperty]
    private string _confirmToggleText = LocalizationService.Instance["Login_ShowPassword"];

    public RegistrationViewModel(AuthService authService)
    {
        _authService = authService;
    }

    partial void OnIsPasswordVisibleChanged(bool value)
    {
        PasswordToggleText = value
            ? LocalizationService.Instance["Login_HidePassword"]
            : LocalizationService.Instance["Login_ShowPassword"];
    }

    partial void OnIsConfirmPasswordVisibleChanged(bool value)
    {
        ConfirmToggleText = value
            ? LocalizationService.Instance["Login_HidePassword"]
            : LocalizationService.Instance["Login_ShowPassword"];
    }

    [RelayCommand]
    private void TogglePassword()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }

    [RelayCommand]
    private void ToggleConfirm()
    {
        IsConfirmPasswordVisible = !IsConfirmPasswordVisible;
    }

    [RelayCommand]
    private async Task Register()
    {
        HasError = false;

        if (string.IsNullOrWhiteSpace(FullName))
        {
            ErrorText = LocalizationService.Instance["Register_NameRequired"];
            HasError = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(Email) || !PageUtility.IsValidEmail(Email))
        {
            ErrorText = LocalizationService.Instance["Register_InvalidEmail"];
            HasError = true;
            return;
        }

        if (Password != ConfirmPassword)
        {
            ErrorText = LocalizationService.Instance["Register_PasswordMismatch"];
            HasError = true;
            return;
        }

        IsBusy = true;

        try
        {
            var rdto = new RegisterRequest(Email.Trim(), Password, FullName.Trim());
            var result = await _authService.RegisterAsync(rdto);

            if (result.Success)
            {
                await Shell.Current.DisplayAlert(
                    LocalizationService.Instance["Register_Success"],
                    LocalizationService.Instance["Register_SuccessMessage"],
                    LocalizationService.Instance["App_Continue"]);
                await Shell.Current.GoToAsync("///login");
            }
            else
            {
                ErrorText = ApiMessageMapper.Localize(result.Code, result.Message)
                    ?? LocalizationService.Instance["Register_Failed"];
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            ErrorText = LocalizationService.Instance["Register_Failed"] + " " + ex.Message;
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GoToLogin()
    {
        await Shell.Current.GoToAsync("///login");
    }
}
