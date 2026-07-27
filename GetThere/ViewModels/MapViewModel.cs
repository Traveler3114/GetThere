using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GetThere.ViewModels;

public partial class MapViewModel : BaseViewModel
{
    [ObservableProperty]
    private string _mapUrl = string.Empty;

    public MapViewModel() { }

    [RelayCommand]
    private void LoadMap()
    {
        var mapUrl = Helpers.ApiEndpoints.MapPageUrl;

        Trace.WriteLine($"[MapViewModel] Loading map: {mapUrl}");
        MapUrl = mapUrl;
    }
}
