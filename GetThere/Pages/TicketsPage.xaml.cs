using GetThere.Helpers;
using GetThere.Localization;
using GetThere.Services;

namespace GetThere.Pages;

public partial class TicketsPage : ContentPage
{
    private readonly AuthService _authService;

    public TicketsPage(AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await UpdateAuthStateAsync();
    }

    private async Task UpdateAuthStateAsync()
    {
        var token = await _authService.GetTokenAsync();

        if (string.IsNullOrWhiteSpace(token) || AuthService.IsGuest())
        {
            StatusLabel.Text = LocalizationService.Instance["Tickets_AccountRequired"];
            DescriptionLabel.Text = LocalizationService.Instance["Tickets_AccountRequiredDesc"];
        }
        else
        {
            StatusLabel.Text = LocalizationService.Instance["Tickets_ComingSoon"];
            DescriptionLabel.Text = LocalizationService.Instance["Tickets_ComingSoonDesc"];
        }
    }
}
