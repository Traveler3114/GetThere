using System.Diagnostics;
using System.Text.Json;

using GetThere.Services;
using GetThere.ViewModels;

namespace GetThere.Pages;

public partial class MapPage : ContentPage
{
    private readonly MapViewModel _viewModel;
    private readonly AuthService _authService;

    public MapPage(MapViewModel viewModel, AuthService authService)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _authService = authService;
        _viewModel.ModeFilterChanged += OnModeFilterChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadMapCommand.Execute(null);
    }

    /// <summary>
    /// Hands the page its access token once it has loaded.
    /// <para>
    /// The map calls GetThereAPI's authenticated proxy, but a WebView navigation cannot carry an
    /// Authorization header. Pushing the token in afterwards keeps it out of the URL, where it would
    /// otherwise end up in server request logs and WebView history. The page holds its requests until
    /// <c>setAuthToken</c> is called.
    /// </para>
    /// </summary>
    private async void OnMapNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success) return;

        try
        {
            var token = await _authService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return;

            // EvaluateJavaScriptAsync does not quote arguments for us; the token is base64url so it
            // carries no quotes, but escape defensively rather than relying on that.
            var escaped = token.Replace("\\", "\\\\", StringComparison.Ordinal)
                               .Replace("'", "\\'", StringComparison.Ordinal);

            await MapWebView.EvaluateJavaScriptAsync($"window.setAuthToken && window.setAuthToken('{escaped}')");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] Could not hand the token to the map: {ex.Message}");
        }

        // The chips are drawn natively over the page, so the page starts out showing everything.
        // Replay the current selection now that there is something to talk to.
        _viewModel.PublishModeFilter();
    }

    private async void OnModeFilterChanged(object? sender, IReadOnlyList<string> modes)
    {
        try
        {
            // Serialised rather than concatenated: the keys are ours today, but a hand-built array
            // literal is the kind of thing that quietly becomes an injection point later.
            var json = JsonSerializer.Serialize(modes);
            await MapWebView.EvaluateJavaScriptAsync($"window.setModeFilter && window.setModeFilter({json})");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] Could not apply the mode filter: {ex.Message}");
        }
    }

    private async void OnRecentreTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await MapWebView.EvaluateJavaScriptAsync("window.recentreMap && window.recentreMap()");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] Could not recentre: {ex.Message}");
        }
    }

    private async void OnLayersTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await MapWebView.EvaluateJavaScriptAsync("window.toggleRouteLines && window.toggleRouteLines()");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] Could not switch layers: {ex.Message}");
        }
    }
}
