using GetThere.Services;
using GetThereShared.Dtos;
using System.Diagnostics;
using System.Text.Json;

namespace GetThere.Pages;

public partial class MapPage : ContentPage
{
    private readonly GtfsService     _gtfsApi;
    private readonly OperatorService _operatorService;
    private System.Timers.Timer?     _realtimeTimer;

    // Full operator objects kept for realtime polling (need format/auth config)
    private List<TransitOperatorDto> _activeRealtimeOperators = [];
    private Dictionary<string, int>  _routeTypeMap            = [];

    public MapPage(GtfsService gtfsApi, OperatorService operatorService)
    {
        InitializeComponent();
        _gtfsApi         = gtfsApi;
        _operatorService = operatorService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await GetLocationAndUpdateMap();
        await LoadGtfsAsync();
        StartRealtimePolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopRealtimePolling();
    }

    // ────────────────────────────────────────────────────────────────────
    // Location
    // ────────────────────────────────────────────────────────────────────

    private async Task GetLocationAndUpdateMap()
    {
        try
        {
            LoadingOverlay.IsVisible = true;
            await Task.Delay(500);
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status == PermissionStatus.Granted)
            {
                var location = await Geolocation.GetLocationAsync(new GeolocationRequest
                {
                    DesiredAccuracy = GeolocationAccuracy.Best,
                    Timeout         = TimeSpan.FromSeconds(10)
                });
                if (location != null)
                {
                    var script =
                        $"updateMapLocation(" +
                        $"{location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
                        $"{location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});";
                    await MapWebView.EvaluateJavaScriptAsync(script);
                }
            }
        }
        catch (Exception ex) { Trace.WriteLine($"[Map] Location error: {ex.Message}"); }
        finally { LoadingOverlay.IsVisible = false; }
    }

    // ────────────────────────────────────────────────────────────────────
    // Load static GTFS
    // ────────────────────────────────────────────────────────────────────

    private async Task LoadGtfsAsync()
    {
        try
        {
            var operators = await _operatorService.GetAllAsync();
            Trace.WriteLine($"[Map] Got {operators.Count} operators");

            _activeRealtimeOperators.Clear();
            _routeTypeMap.Clear();
            var allStops  = new List<object>();
            var allRoutes = new List<object>();

            foreach (var op in operators)
            {
                if (!_gtfsApi.IsInstalled(op.Id)) continue;

                var stops  = await _gtfsApi.ParseStopsAsync(op.Id);
                var routes = await _gtfsApi.ParseRoutesAsync(op.Id);
                Trace.WriteLine($"[Map] {op.Name}: {stops.Count} stops, {routes.Count} routes");

                allStops.AddRange(stops.Select(s => new
                    { stopId = s.StopId, name = s.Name, lat = s.Lat, lon = s.Lon }));

                allRoutes.AddRange(routes.Select(r => new
                    { routeId = r.RouteId, shortName = r.ShortName, color = r.Color,
                      shape = r.Shape, routeType = r.RouteType }));

                foreach (var r in routes)
                    _routeTypeMap[r.RouteId] = r.RouteType;

                if (_gtfsApi.HasRealtime(op.Id))
                    _activeRealtimeOperators.Add(op);
            }

            await CallJs("renderStops",  allStops);
            await CallJs("renderRoutes", allRoutes);
        }
        catch (Exception ex) { Trace.WriteLine($"[Map] Load error: {ex.Message}"); }
    }

    // ────────────────────────────────────────────────────────────────────
    // Realtime polling
    // ────────────────────────────────────────────────────────────────────

    private void StartRealtimePolling()
    {
        if (_activeRealtimeOperators.Count == 0) return;
        Trace.WriteLine($"[Map] Starting realtime — {_activeRealtimeOperators.Count} operators");

        _realtimeTimer = new System.Timers.Timer(15_000);
        _realtimeTimer.Elapsed += async (_, _) => await PollRealtimeAsync();
        _realtimeTimer.AutoReset = true;
        _realtimeTimer.Start();
        Task.Run(PollRealtimeAsync); // immediate first poll
    }

    private void StopRealtimePolling()
    {
        _realtimeTimer?.Stop();
        _realtimeTimer?.Dispose();
        _realtimeTimer = null;
    }

    private async Task PollRealtimeAsync()
    {
        try
        {
            var allVehicles = new List<object>();

            foreach (var op in _activeRealtimeOperators)
            {
                // Pass the full operator — parser needs format + auth config
                var vehicles = await _gtfsApi.GetVehiclesAsync(op);
                Trace.WriteLine($"[Realtime] {op.Name}: {vehicles.Count} vehicles");

                allVehicles.AddRange(vehicles.Select(v => new
                {
                    vehicleId = v.VehicleId,
                    routeId   = v.RouteId,
                    lat       = v.Lat,
                    lon       = v.Lon,
                    bearing   = v.Bearing,
                    label     = v.Label,
                    routeType = (!string.IsNullOrEmpty(v.RouteId)
                                 && _routeTypeMap.TryGetValue(v.RouteId!, out var rt)) ? rt : 3
                }));
            }

            await MainThread.InvokeOnMainThreadAsync(async () =>
                await CallJs("renderVehicles", allVehicles));
        }
        catch (Exception ex) { Trace.WriteLine($"[Realtime] Poll error: {ex.Message}"); }
    }

    // ────────────────────────────────────────────────────────────────────
    // JS bridge
    // ────────────────────────────────────────────────────────────────────

    private async Task CallJs(string fn, object data)
    {
        var json    = JsonSerializer.Serialize(data);
        var escaped = json.Replace("\\", "\\\\").Replace("'", "\\'");
        await MapWebView.EvaluateJavaScriptAsync($"{fn}('{escaped}')");
    }
}
