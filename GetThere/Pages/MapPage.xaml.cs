using GetThere.Services;
using System.Diagnostics;
using System.Text.Json;

namespace GetThere.Pages;

public partial class MapPage : ContentPage
{
    private readonly GtfsService _gtfsApi;
    private readonly OperatorService _operatorService;
    private System.Timers.Timer? _realtimeTimer;
    private List<int> _activeRealtimeOperatorIds = [];
    private Dictionary<string, int> _routeTypeMap = [];

    public MapPage(GtfsService gtfsApi, OperatorService operatorService)
    {
        InitializeComponent();
        _gtfsApi = gtfsApi;
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
                    Timeout = TimeSpan.FromSeconds(10)
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

    private async Task LoadGtfsAsync()
    {
        try
        {
            var operators = await _operatorService.GetAllAsync();
            Trace.WriteLine($"[Map] Got {operators.Count} operators from API");

            _activeRealtimeOperatorIds.Clear();
            _routeTypeMap.Clear();
            var allStops = new List<object>();
            var allRoutes = new List<object>();

            foreach (var op in operators)
            {
                Trace.WriteLine($"[Map] Operator {op.Id} '{op.Name}' — Installed={_gtfsApi.IsInstalled(op.Id)}, HasRealtime={_gtfsApi.HasRealtime(op.Id)}, RealtimeUrl={op.GtfsRealtimeFeedUrl}");

                if (!_gtfsApi.IsInstalled(op.Id)) continue;

                var stops = await _gtfsApi.ParseStopsAsync(op.Id);
                var routes = await _gtfsApi.ParseRoutesAsync(op.Id);
                Trace.WriteLine($"[Map] Operator {op.Id}: {stops.Count} stops, {routes.Count} routes");

                allStops.AddRange(stops.Select(s => new
                { stopId = s.StopId, name = s.Name, lat = s.Lat, lon = s.Lon }));

                allRoutes.AddRange(routes.Select(r => new
                {
                    routeId = r.RouteId,
                    shortName = r.ShortName,
                    color = r.Color,
                    shape = r.Shape,
                    routeType = r.RouteType
                }));

                foreach (var r in routes)
                    _routeTypeMap[r.RouteId] = r.RouteType;

                if (_gtfsApi.HasRealtime(op.Id))
                {
                    _activeRealtimeOperatorIds.Add(op.Id);
                    Trace.WriteLine($"[Map] Operator {op.Id} queued for realtime polling");
                }
                else
                {
                    Trace.WriteLine($"[Map] Operator {op.Id} has NO cached realtime URL — skipping realtime");
                }
            }

            await CallJs("renderStops", allStops);
            await CallJs("renderRoutes", allRoutes);
        }
        catch (Exception ex) { Trace.WriteLine($"[Map] GTFS load error: {ex.Message}"); }
    }

    private void StartRealtimePolling()
    {
        Trace.WriteLine($"[Map] StartRealtimePolling — {_activeRealtimeOperatorIds.Count} operators");
        if (_activeRealtimeOperatorIds.Count == 0) return;

        _realtimeTimer = new System.Timers.Timer(15_000);
        _realtimeTimer.Elapsed += async (_, _) => await PollRealtimeAsync();
        _realtimeTimer.AutoReset = true;
        _realtimeTimer.Start();
        Task.Run(PollRealtimeAsync);
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
            Trace.WriteLine($"[Realtime] Polling {_activeRealtimeOperatorIds.Count} operators...");
            var allVehicles = new List<object>();

            foreach (var opId in _activeRealtimeOperatorIds)
            {
                var vehicles = await _gtfsApi.GetVehiclesAsync(opId);
                Trace.WriteLine($"[Realtime] Operator {opId}: {vehicles.Count} vehicles");

                allVehicles.AddRange(vehicles.Select(v => new
                {
                    vehicleId = v.VehicleId,
                    routeId = v.RouteId,
                    lat = v.Lat,
                    lon = v.Lon,
                    bearing = v.Bearing,
                    label = v.Label,
                    routeType = !string.IsNullOrEmpty(v.RouteId) && _routeTypeMap.TryGetValue(v.RouteId, out var rt) ? rt : 3
                }));
            }

            Trace.WriteLine($"[Realtime] Total vehicles to render: {allVehicles.Count}");
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await CallJs("renderVehicles", allVehicles));
        }
        catch (Exception ex) { Trace.WriteLine($"[Realtime] Poll error: {ex.GetType().Name}: {ex.Message}"); }
    }

    private async Task CallJs(string fn, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var escaped = json.Replace("\\", "\\\\").Replace("'", "\\'");
        await MapWebView.EvaluateJavaScriptAsync($"{fn}('{escaped}')");
    }
}