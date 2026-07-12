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
        return "https://10.0.2.2:5001/map/public.html";
#else
        return "https://localhost:5001/map/public.html";
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
