#nullable enable
#pragma warning disable CA1416

using GetThere.Helpers;
using GetThere.Localization;
using GetThere.Services;
using GetThereShared.Contracts;

namespace GetThere.Pages;

public partial class RegistrationPage : ContentPage
{
    private readonly AuthService _authService;

    public RegistrationPage(AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
        SizeChanged += OnPageSizeChanged;
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        PageUtility.ApplyTicketsStyleResponsive(Width, RegistrationCard);
    }

    private async void RegisterButton_Clicked(object? sender, EventArgs e)
    {
        PageUtility.HideError(ErrorLabel);

        var rdto = new RegisterRequest
        {
            FullName = FullNameEntry.Text?.Trim() ?? string.Empty,
            Email = EmailEntry.Text?.Trim() ?? string.Empty,
            Password = PasswordEntry.Text ?? string.Empty,
        };

        string confirm = ConfirmPasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rdto.FullName))
        {
            PageUtility.ShowError(ErrorLabel, LocalizationService.Instance["Register_NameRequired"]);
            return;
        }

        if (string.IsNullOrWhiteSpace(rdto.Email) || !PageUtility.IsValidEmail(rdto.Email))
        {
            PageUtility.ShowError(ErrorLabel, LocalizationService.Instance["Register_InvalidEmail"]);
            return;
        }

        if (rdto.Password.Length < 8)
        {
            PageUtility.ShowError(ErrorLabel, LocalizationService.Instance["Register_PasswordTooShort"]);
            return;
        }

        if (rdto.Password != confirm)
        {
            PageUtility.ShowError(ErrorLabel, LocalizationService.Instance["Register_PasswordMismatch"]);
            return;
        }

        PageUtility.SetBusy(BusyIndicator, RegisterButton, true);

        try
        {
            var result = await _authService.RegisterAsync(rdto);

            if (result.Success)
            {
                await DisplayAlertAsync(LocalizationService.Instance["Register_Success"], LocalizationService.Instance["Register_SuccessMessage"], LocalizationService.Instance["App_Continue"]);
                await Shell.Current.GoToAsync("///login");
            }
            else
            {
                PageUtility.ShowError(ErrorLabel, result.Message ?? LocalizationService.Instance["Register_Failed"]);
            }
        }
        catch (Exception ex)
        {
            PageUtility.ShowError(ErrorLabel, LocalizationService.Instance["Register_Failed"] + " " + ex.Message);
        }
        finally
        {
            PageUtility.SetBusy(BusyIndicator, RegisterButton, false);
        }
    }

    private async void LoginButton_Clicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("///login");
    }

    private async void LoginButton_Tapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("///login");
    }

    private void TogglePass_Tapped(object? sender, TappedEventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        TogglePassBtn.Text = PasswordEntry.IsPassword ? LocalizationService.Instance["Login_ShowPassword"] : LocalizationService.Instance["Login_HidePassword"];
    }

    private void ToggleConfirm_Tapped(object? sender, TappedEventArgs e)
    {
        ConfirmPasswordEntry.IsPassword = !ConfirmPasswordEntry.IsPassword;
        ToggleConfirmBtn.Text = ConfirmPasswordEntry.IsPassword ? LocalizationService.Instance["Login_ShowPassword"] : LocalizationService.Instance["Login_HidePassword"];
    }
}
