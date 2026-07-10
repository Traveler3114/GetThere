using System.Diagnostics;

namespace GetThere.Pages;

public partial class MapPage : ContentPage
{
    public MapPage()
    {
        InitializeComponent();
    }

    private static string GetTransitMapUrl()
    {
#if ANDROID
        return "http://10.0.2.2:5000/map/";
#else
        return "http://localhost:5000/map/";
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var url = GetTransitMapUrl();
        Trace.WriteLine($"[MapPage] Loading map: {url}");
        await MainThread.InvokeOnMainThreadAsync(() =>
            MapWebView.Source = new UrlWebViewSource { Url = url });
    }
}
