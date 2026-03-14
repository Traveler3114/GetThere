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

    // Full operator objects kept for realtime polling (need format/auth config)
    private List<TransitOperatorDto> _activeRealtimeOperators = [];
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
            var allStops = new List<object>();
            var allRoutes = new List<object>();
            var allStopSchedules = new List<object>();

            foreach (var op in operators)
            {
                if (!_gtfsApi.IsInstalled(op.Id)) continue;

                var stops = await _gtfsApi.ParseStopsAsync(op.Id);
                var stop_schedules = await _gtfsApi.ParseStopTimesAsync(op.Id);
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

                allStopSchedules.AddRange(stop_schedules.Select(ss => new
                {
                    tripId = ss.TripId,
                    arrivalTime = ss.ArrivalTime,
                    departureTime = ss.DepartureTime,
                    destination = ss.Destination,
                    stopId = ss.StopId,
                }));



                foreach (var r in routes)
                    _routeTypeMap[r.RouteId] = r.RouteType;

                if (_gtfsApi.HasRealtime(op.Id))
                    _activeRealtimeOperators.Add(op);
            }

            await CallJs("renderStops", allStops);
            await CallJs("renderRoutes", allRoutes);
            await CallJs("renderStopSchedules", allStopSchedules);
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
            // Drop any operators that have been uninstalled since last poll
            var stillInstalled = _activeRealtimeOperators
                .Where(op => _gtfsApi.IsInstalled(op.Id))
                .ToList();

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
                Trace.WriteLine($"[Realtime] {op.Name}: {vehicles.Count} vehicles");

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

    private async void OnSimulateLocationClicked(object sender, EventArgs e)
    {
        try
        {
            var location = await Geolocation.GetLastKnownLocationAsync() ?? await Geolocation.GetLocationAsync();
            if (location == null)
            {
                // Default to Zagreb if no location found
                location = new Location(45.8150, 15.9819);
            }

            // Move roughly 5km East
            double lat = location.Latitude;
            double lon = location.Longitude + 0.065; // ~5km at this latitude
                
            var script = $"updateMapLocation({lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {lat.ToString(System.Globalization.CultureInfo.InvariantCulture)});";
            await MapWebView.EvaluateJavaScriptAsync(script);
            await DisplayAlertAsync("GPS Simulator", $"Location updated (+5km East).\nNew: {lat:F4}, {lon:F4}", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", "Could not simulate location: " + ex.Message, "OK");
        }
    }








    // ────────────────────────────────────────────────────────────────────
    // JS bridge
    // ────────────────────────────────────────────────────────────────────

    private async Task CallJs(string fn, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var escaped = json.Replace("\\", "\\\\").Replace("'", "\\'");
        await MapWebView.EvaluateJavaScriptAsync($"{fn}('{escaped}')");
    }



}