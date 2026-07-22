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
#if ANDROID
        var transitApi = "https://10.0.2.2:5001";
        var mapUrl = "https://10.0.2.2:7230/map/public.html?api=" + transitApi;
#else
        var transitApi = "https://localhost:5001";
        var mapUrl = "https://localhost:7230/map/public.html?api=" + transitApi;
#endif
        Trace.WriteLine($"[MapViewModel] Loading map: {mapUrl}");
        MapUrl = mapUrl;
    }
}
