using System.Diagnostics;
using System.Text.Json;

using GetThere.Services;
using GetThere.ViewModels;

using GetThereShared.Contracts;

namespace GetThere.Pages;

/// <summary>
/// Hosts the map page in a WebView.
/// <para>
/// The page is served by TransitInfoAPI and reads it same-origin as an anonymous browser, so there
/// is no token to hand over after navigation, and its controls are its own — this class used to
/// carry four <c>EvaluateJavaScriptAsync</c> bridges for chrome that has since moved into the page.
/// </para>
/// <para>
/// The one bridge left is one-way, in the other direction: the page's "Get tickets" button navigates
/// to the <c>gtapp://journey</c> scheme, and <see cref="OnWebViewNavigating"/> cancels that
/// navigation and passes the itinerary to <see cref="JourneyHandoff"/> so the app can price and buy
/// it through GetThereAPI.
/// </para>
/// </summary>
public partial class MapPage : ContentPage
{
    private readonly MapViewModel _viewModel;
    private readonly JourneyHandoff _handoff;

    public MapPage(MapViewModel viewModel, JourneyHandoff handoff)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _handoff = handoff;

        MapWebView.Navigating += OnWebViewNavigating;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Re-run on every appearance rather than once in the constructor: the URL carries the
        // current language, so returning to this tab after a language change reloads the page in it.
        _viewModel.LoadMapCommand.Execute(null);
    }

    /// <summary>
    /// The WebView→native handoff. The map page emits the selected itinerary through the
    /// <c>gtapp://journey</c> scheme; we cancel the navigation, read the <c>legs</c> payload and open
    /// the native buy flow.
    /// <para>
    /// Nothing else is intercepted, and nothing is ever injected into the page — the removed
    /// native→JavaScript chrome/token bridge stays removed.
    /// </para>
    /// </summary>
    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        const string prefix = "gtapp://journey?";
        if (!e.Url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return;

        e.Cancel = true;

        try
        {
            var legsParam = ParseQueryParameter(e.Url, "legs");
            if (string.IsNullOrEmpty(legsParam))
                return;

            var legs = JsonSerializer.Deserialize<List<QuoteLegDto>>(
                Uri.UnescapeDataString(legsParam), JourneyHandoff.JsonOptions);

            if (legs is null || legs.Count == 0)
                return;

            _handoff.PendingLegs = legs;
            Trace.WriteLine($"[MapPage] itinerary handoff: {legs.Count} legs, opening buy flow");
            _ = Shell.Current.GoToAsync("buyjourney");
        }
        catch (Exception ex)
        {
            // A malformed handoff must not take the map down — drop it and stay put.
            _handoff.PendingLegs = null;
            Trace.WriteLine($"[MapPage] journey handoff ignored: {ex}");
        }
    }

    private static string? ParseQueryParameter(string url, string key)
    {
        var queryStart = url.IndexOf('?');
        if (queryStart < 0)
            return null;

        var query = url[(queryStart + 1)..];
        foreach (var part in query.Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;

            if (string.Equals(part[..eq], key, StringComparison.OrdinalIgnoreCase))
                return part[(eq + 1)..];
        }

        return null;
    }
}
