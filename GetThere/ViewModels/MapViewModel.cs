using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;

namespace GetThere.ViewModels;

/// <summary>
/// Backs the map screen, which is a WebView and nothing else.
/// <para>
/// This used to own the transport-mode chips and push them into the page through the host. The page
/// owns its own chrome now, so all that is left is deciding which URL to load.
/// </para>
/// </summary>
public partial class MapViewModel : BaseViewModel
{
    [ObservableProperty]
    private string _mapUrl = string.Empty;

    [RelayCommand]
    private void LoadMap()
    {
        // The page labels itself from this, so the map follows the app's language setting.
        var lang = LocalizationService.Instance.CurrentCulture.TwoLetterISOLanguageName;
        var mapUrl = Helpers.ApiEndpoints.MapPageUrl(lang);

        Trace.WriteLine($"[MapViewModel] Loading map: {mapUrl}");
        MapUrl = mapUrl;
    }
}
