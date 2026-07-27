using System.Collections.ObjectModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;

namespace GetThere.ViewModels;

/// <summary>
/// One transport-mode chip on the map. <see cref="Key"/> is the value handed to the map page's
/// <c>setModeFilter</c>, so it must stay in step with MODE_ROUTE_TYPES in public.html.
/// </summary>
public partial class MapModeChip : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public required string Key { get; init; }
    public required string Label { get; init; }
}

public partial class MapViewModel : BaseViewModel
{
    [ObservableProperty]
    private string _mapUrl = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Raised when the chip selection changes so the page can push it into the WebView.
    /// The view model has no WebView reference of its own, so it reports rather than calls.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? ModeFilterChanged;

    public ObservableCollection<MapModeChip> Modes { get; } = [];

    public MapViewModel()
    {
        // Frame 3a: Tram · Bus · Train · Bikes, with Tram lit on load.
        Modes.Add(new MapModeChip { Key = "Tram", Label = LocalizationService.Instance["Map_ModeTram"], IsSelected = true });
        Modes.Add(new MapModeChip { Key = "Bus", Label = LocalizationService.Instance["Map_ModeBus"] });
        Modes.Add(new MapModeChip { Key = "Train", Label = LocalizationService.Instance["Map_ModeTrain"] });
        Modes.Add(new MapModeChip { Key = "Bikes", Label = LocalizationService.Instance["Map_ModeBikes"] });
    }

    [RelayCommand]
    private void LoadMap()
    {
        var mapUrl = Helpers.ApiEndpoints.MapPageUrl;

        Trace.WriteLine($"[MapViewModel] Loading map: {mapUrl}");
        MapUrl = mapUrl;
    }

    /// <summary>
    /// Toggles a chip. Turning the last one off restores "everything", which is what the map
    /// shows on load — an empty map is never a useful state to be stuck in.
    /// </summary>
    [RelayCommand]
    private void ToggleMode(MapModeChip? chip)
    {
        if (chip is null)
            return;

        chip.IsSelected = !chip.IsSelected;

        if (Modes.All(m => !m.IsSelected))
        {
            foreach (var mode in Modes)
                mode.IsSelected = true;
        }

        PublishModeFilter();
    }

    /// <summary>Pushes the current selection out; also called once the page finishes loading.</summary>
    public void PublishModeFilter()
    {
        var selected = Modes.Where(m => m.IsSelected).Select(m => m.Key).ToList();

        // All-on is the same as no filter, and the map treats an empty list as "show everything".
        if (selected.Count == Modes.Count)
            selected.Clear();

        ModeFilterChanged?.Invoke(this, selected);
    }
}
