using System.Diagnostics;

namespace GetThere.Pages;

public partial class MapPage : ContentPage
{
    public MapPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

#if ANDROID
        var transitApi = "https://10.0.2.2:5001";
        var mapUrl = "https://10.0.2.2:7230/map/public.html?api=" + transitApi;
#else
        var transitApi = "https://localhost:5001";
        var mapUrl = "https://localhost:7230/map/public.html?api=" + transitApi;
#endif

        Trace.WriteLine($"[MapPage] Loading map: {mapUrl}");
        await MainThread.InvokeOnMainThreadAsync(() =>
            MapWebView.Source = new UrlWebViewSource { Url = mapUrl });
    }
}
