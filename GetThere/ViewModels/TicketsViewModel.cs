using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;
using GetThere.Services;

namespace GetThere.ViewModels;

public partial class TicketsViewModel : BaseViewModel
{
    private readonly AuthService _authService;

    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _descriptionText = string.Empty;

    public TicketsViewModel(AuthService authService)
    {
        _authService = authService;
    }

    [RelayCommand]
    private async Task UpdateAuthState()
    {
        var token = await _authService.GetTokenAsync();

        if (string.IsNullOrWhiteSpace(token) || AuthService.IsGuest())
        {
            TitleText = LocalizationService.Instance["Tickets_AccountRequired"];
            DescriptionText = LocalizationService.Instance["Tickets_AccountRequiredDesc"];
        }
        else
        {
            TitleText = LocalizationService.Instance["Tickets_ComingSoon"];
            DescriptionText = LocalizationService.Instance["Tickets_ComingSoonDesc"];
        }
    }
}
