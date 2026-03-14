using GetThere.Services;
using GetThereShared.Dtos;
using System.Diagnostics;
using System.Text.Json;

namespace GetThere.Pages;

public partial class MapPage : ContentPage
{
    private readonly GtfsService _gtfsApi;
    private readonly OperatorService _operatorService;
    private System.Timers.Timer? _realtimeTimer;
    private System.Timers.Timer? _jsMessageTimer;

    private List<TransitOperatorDto> _activeRealtimeOperators = [];
    private Dictionary<string, int> _routeTypeMap = [];
    private Dictionary<string, int> _stopOperatorMap = [];

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
        StartJsMessagePolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopRealtimePolling();
        StopJsMessagePolling();
    }

    // ────────────────────────────────────────────────────────────────────
    // JS → C# message polling
    // JS sets: window._pendingMsg = "stopSchedule:STOPID"
    // C# reads and clears it every 300ms
    // ────────────────────────────────────────────────────────────────────

    private void StartJsMessagePolling()
    {
        _jsMessageTimer = new System.Timers.Timer(300);
        _jsMessageTimer.Elapsed += async (_, _) => await PollJsMessagesAsync();
        _jsMessageTimer.AutoReset = true;
        _jsMessageTimer.Start();
    }

    private void StopJsMessagePolling()
    {
        _jsMessageTimer?.Stop();
        _jsMessageTimer?.Dispose();
        _jsMessageTimer = null;
    }

    private async Task PollJsMessagesAsync()
    {
        try
        {
            string? msg = null;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                // Read and atomically clear the pending message
                var raw = await MapWebView.EvaluateJavaScriptAsync(
                    "(function(){ var m = window._pendingMsg || ''; window._pendingMsg = ''; return m; })()");
                // EvaluateJavaScriptAsync returns the value JSON-encoded as a string
                // so it comes back with surrounding quotes — strip them
                if (!string.IsNullOrEmpty(raw) && raw != "null" && raw != "\"\"")
                    msg = raw.Trim('"');
            });

            if (string.IsNullOrEmpty(msg)) return;

            Trace.WriteLine($"[MAP/POLL] Got JS message: '{msg}'");

            if (msg.StartsWith("stopSchedule:", StringComparison.Ordinal))
            {
                var stopId = msg["stopSchedule:".Length..];
                await HandleStopScheduleAsync(stopId);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MAP/POLL] Error: {ex.Message}");
        }
    }

    private async Task HandleStopScheduleAsync(string stopId)
    {
        Trace.WriteLine($"[MAP/SCHED] Handling stop '{stopId}', map has {_stopOperatorMap.Count} stops");

        if (!_stopOperatorMap.TryGetValue(stopId, out var operatorId))
        {
            Trace.WriteLine($"[MAP/SCHED] Stop '{stopId}' not found in operator map — sending empty");
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await CallJs("renderStopSchedule", new { stopId, groups = Array.Empty<object>() }));
            return;
        }

        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            Trace.WriteLine($"[MAP/SCHED] Querying op={operatorId} stop='{stopId}' date={today}");
            var groups = await _gtfsApi.ParseStopScheduleAsync(operatorId, stopId, today);
            Trace.WriteLine($"[MAP/SCHED] Got {groups.Count} groups, sending to JS");
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await CallJs("renderStopSchedule", new { stopId, groups }));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MAP/SCHED] Exception: {ex.Message}");
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await CallJs("renderStopSchedule", new { stopId, groups = Array.Empty<object>() }));
        }
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
            _stopOperatorMap.Clear();

            var allStops = new List<object>();
            var allRoutes = new List<object>();

            foreach (var op in operators)
            {
                if (!_gtfsApi.IsInstalled(op.Id)) continue;

                var stops = await _gtfsApi.ParseStopsAsync(op.Id);
                var routes = await _gtfsApi.ParseRoutesAsync(op.Id);
                Trace.WriteLine($"[Map] {op.Name}: {stops.Count} stops, {routes.Count} routes");

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

                foreach (var s in stops)
                    _stopOperatorMap.TryAdd(s.StopId, op.Id);

                if (_gtfsApi.HasRealtime(op.Id))
                    _activeRealtimeOperators.Add(op);
            }

            await CallJs("renderStops", allStops);
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
            var stillInstalled = _activeRealtimeOperators
                .Where(op => _gtfsApi.IsInstalled(op.Id)).ToList();

            if (stillInstalled.Count != _activeRealtimeOperators.Count)
            {
                _activeRealtimeOperators = stillInstalled;
                if (_activeRealtimeOperators.Count == 0)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                        await CallJs("clearVehicles", new object()));
                    return;
                }
            }

            var allVehicles = new List<object>();
            foreach (var op in _activeRealtimeOperators)
            {
                var vehicles = await _gtfsApi.GetVehiclesAsync(op);
                allVehicles.AddRange(vehicles.Select(v => new
                {
                    vehicleId = v.VehicleId,
                    routeId = v.RouteId,
                    lat = v.Lat,
                    lon = v.Lon,
                    bearing = v.Bearing,
                    label = v.Label,
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
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var base64 = Convert.ToBase64String(bytes);
        await MapWebView.EvaluateJavaScriptAsync(
            $"{fn}(JSON.parse(atob('{base64}')))");
    }
}