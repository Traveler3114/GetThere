using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;
using GetThere.Services;

namespace GetThere.ViewModels;

public partial class ShopViewModel : BaseViewModel
{
    private readonly AuthService _authService;

    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _descriptionText = string.Empty;

    public ShopViewModel(AuthService authService)
    {
        _authService = authService;
    }

    [RelayCommand]
    private async Task UpdateAuthState()
    {
        var token = await _authService.GetTokenAsync();

        if (string.IsNullOrWhiteSpace(token) || AuthService.IsGuest())
        {
            TitleText = LocalizationService.Instance["Shop_AccountRequired"];
            DescriptionText = LocalizationService.Instance["Shop_AccountRequiredDesc"];
        }
        else
        {
            TitleText = LocalizationService.Instance["Shop_ComingSoon"];
            DescriptionText = LocalizationService.Instance["Shop_ComingSoonDesc"];
        }
    }
}
