using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class ProfilePage : ContentPage
{
    public ProfilePage(ProfileViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Application.Current!.RequestedThemeChanged -= OnRequestedThemeChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ProfileViewModel vm && !vm.IsGuest)
            vm.LoadProfileCommand.Execute(null);
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        var isDark = e.RequestedTheme == AppTheme.Dark;
        // Icons use AppThemeBinding — no manual updates needed
    }

    // async void is forced by the Tapped event signature. The try/catch is what keeps a failure here
    // from becoming an unhandled exception on the UI thread — every other handler in the app has one
    // and this was the exception.
    private async void OnAvatarClicked(object? sender, TappedEventArgs e)
    {
        try
        {
            var action = await DisplayActionSheetAsync(
                Localization.LocalizationService.Instance["Profile_PhotoTitle"],
                Localization.LocalizationService.Instance["App_Cancel"], null,
                Localization.LocalizationService.Instance["Profile_PhotoTake"],
                Localization.LocalizationService.Instance["Profile_PhotoUpload"]);

            if (action == Localization.LocalizationService.Instance["Profile_PhotoTake"]
                || action == Localization.LocalizationService.Instance["Profile_PhotoUpload"])
            {
                await DisplayAlertAsync(
                    Localization.LocalizationService.Instance["Profile_PhotoTitle"],
                    Localization.LocalizationService.Instance["Profile_PhotoResult"],
                    Localization.LocalizationService.Instance["App_Ok"]);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[ProfilePage] Avatar picker failed: {ex.Message}");
        }
    }
}
