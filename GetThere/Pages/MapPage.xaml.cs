#nullable enable
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

    private readonly object _realtimeLock = new();
    // tripId → vehicle (for position + stop time updates)
    private Dictionary<string, VehiclePositionDto> _vehiclesByTrip = [];
    // tripId → operatorId (built from trip→route map at load time)
    private Dictionary<string, int> _tripOperatorMap = [];



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
    // JS → C# polling  (JS sets window._pendingMsg, we read every 300ms)
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
            string? raw = null;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                raw = await MapWebView.EvaluateJavaScriptAsync(
                    "(function(){ var m = window._pendingMsg || ''; window._pendingMsg = ''; return m; })()");
            });

            if (string.IsNullOrEmpty(raw) || raw == "null") return;
            Trace.WriteLine($"[MAP/POLL] raw={raw}");

            // Strip JSON-style surrounding quotes that EvaluateJavaScriptAsync adds
            var msg = raw.Trim();
            if (msg.Length >= 2 && msg[0] == '"' && msg[^1] == '"')
                msg = msg[1..^1];

            if (string.IsNullOrEmpty(msg)) return;

            Trace.WriteLine($"[MAP/POLL] msg='{msg}'");

            if (msg.StartsWith("stopSchedule:", StringComparison.Ordinal))
                await HandleStopScheduleAsync(msg["stopSchedule:".Length..]);
            else if (msg.StartsWith("tripDetail:", StringComparison.Ordinal))
                await HandleTripDetailAsync(msg["tripDetail:".Length..]);
        }
        catch (Exception ex) { Trace.WriteLine($"[MAP/POLL] Error: {ex.Message}"); }
    }

    // ────────────────────────────────────────────────────────────────────
    // Stop schedule
    // ────────────────────────────────────────────────────────────────────

    private async Task HandleStopScheduleAsync(string stopId)
    {
        Trace.WriteLine($"[Sched] Request for stop '{stopId}'");
        if (!_stopOperatorMap.TryGetValue(stopId, out var operatorId))
        {
            Trace.WriteLine($"[Sched] Stop not found in map (map has {_stopOperatorMap.Count} entries)");
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await CallJs("renderStopSchedule", new { stopId, groups = Array.Empty<object>() }));
            return;
        }

        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            Trace.WriteLine($"[Sched] Querying op={operatorId} date={today}");
            var groups = await _gtfsApi.ParseStopScheduleAsync(operatorId, stopId, today);
            Trace.WriteLine($"[Sched] Got {groups.Count} groups");

            // Annotate with realtime delay data from TripUpdates
            lock (_realtimeLock)
            {
                foreach (var g in groups)
                    foreach (var d in g.Departures)
                    {
                        if (!_vehiclesByTrip.TryGetValue(d.TripId, out var vehicle)) continue;

                        // Only mark as tracked if the vehicle is actively reporting GPS.
                        // IsScheduledOnly = TripUpdate exists but no VehiclePosition yet —
                        // the vehicle hasn't started its trip or isn't transmitting location.
                        d.IsTracked = !vehicle.IsScheduledOnly;

                        // Find the StopTimeUpdate for this specific stop
                        var stu = vehicle.StopTimeUpdates?
                            .FirstOrDefault(u => u.StopId == stopId);

                        if (stu == null) continue;

                        int delaySec = stu.DelaySeconds;
                        d.DelayMinutes = (int)Math.Round(delaySec / 60.0);

                        // Calculate estimated arrival time
                        var schParts = d.ScheduledTime.Split(':');
                        if (schParts.Length >= 2
                            && int.TryParse(schParts[0], out int sh)
                            && int.TryParse(schParts[1], out int sm))
                        {
                            int estMins = sh * 60 + sm + (int)Math.Round(delaySec / 60.0);
                            // Clamp to valid time range
                            estMins = Math.Max(0, estMins);
                            d.EstimatedTime = $"{estMins / 60:D2}:{estMins % 60:D2}";
                        }
                    }
            }

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
                    await MapWebView.EvaluateJavaScriptAsync(
                        $"updateMapLocation(" +
                        $"{location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"{location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
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
            _activeRealtimeOperators.Clear();
            _routeTypeMap.Clear();
            _stopOperatorMap.Clear();

            var allStops = new List<object>();
            var allRoutes = new List<object>();

            foreach (var op in operators)
            {
                if (!_gtfsApi.IsInstalled(op.Id)) continue;

                var cacheFile = Path.Combine(_gtfsApi.GetOperatorDir(op.Id), "stop_route_types.json");
                if (!File.Exists(cacheFile))
                    await _gtfsApi.BuildStopRouteTypeMapAsync(op.Id);

                var stops = await _gtfsApi.ParseStopsAsync(op.Id);
                var stop_schedules = await _gtfsApi.ParseStopTimesAsync(op.Id);
                var routes = await _gtfsApi.ParseRoutesAsync(op.Id);
                Trace.WriteLine($"[Map] {op.Name}: {stops.Count} stops, {routes.Count} routes");


                allStops.AddRange(stops.Select(s => new
                { stopId = s.StopId, name = s.Name, lat = s.Lat, lon = s.Lon, routeType = s.RouteType }));

                allRoutes.AddRange(routes.Select(r => new
                {
                    routeId = r.RouteId,
                    shortName = r.ShortName,
                    color = r.Color,
                    shape = r.Shape,
                    routeType = r.RouteType
                }));

                // (Removed allStopSchedules collection here)



                foreach (var s in stops)
                    _stopOperatorMap[s.StopId] = op.Id;

                foreach (var r in routes)
                {
                    _routeTypeMap[r.RouteId] = r.RouteType;
                    // Note: If we had a direct route->operator map, we could use that.
                    // For now, we'll assume trips belong to the operator they were parsed from.
                }

                var tripMap = _gtfsApi.GetTripRouteMapPublic(op.Id);
                if (tripMap != null)
                {
                    foreach (var tripId in tripMap.Keys)
                        _tripOperatorMap[tripId] = op.Id;
                }

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
            var stillInstalled = _activeRealtimeOperators.Where(op => _gtfsApi.IsInstalled(op.Id)).ToList();
            if (stillInstalled.Count != _activeRealtimeOperators.Count)
            {
                _activeRealtimeOperators = stillInstalled;
                if (_activeRealtimeOperators.Count == 0)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () => await CallJs("clearVehicles", new object()));
                    return;
                }
            }

            var allFetched = new List<VehiclePositionDto>();
            foreach (var op in _activeRealtimeOperators)
                allFetched.AddRange(await _gtfsApi.GetVehiclesAsync(op));

            // Rebuild trip→vehicle map (includes both GPS vehicles and TripUpdate-only entries)
            var newVehiclesByTrip = new Dictionary<string, VehiclePositionDto>(StringComparer.Ordinal);
            foreach (var v in allFetched)
                if (!string.IsNullOrEmpty(v.TripId))
                    newVehiclesByTrip[v.TripId!] = v;

            lock (_realtimeLock)
                _vehiclesByTrip = newVehiclesByTrip;

            // Only send vehicles with actual GPS positions to the map
            var mapVehicles = allFetched
                .Where(v => !v.IsScheduledOnly)
                .Select(v => new
                {
                    vehicleId = v.VehicleId,
                    tripId = v.TripId,   // needed for trip detail panel on vehicle click
                    routeId = v.RouteId,
                    lat = v.Lat,
                    lon = v.Lon,
                    bearing = v.Bearing,
                    label = v.Label,
                    routeType = (!string.IsNullOrEmpty(v.RouteId)
                                 && _routeTypeMap.TryGetValue(v.RouteId!, out var rt)) ? rt : 3
                })
                .ToList();

            Trace.WriteLine($"[Realtime] {mapVehicles.Count} vehicles on map, " +
                            $"{newVehiclesByTrip.Count} total tracked trips");

            await MainThread.InvokeOnMainThreadAsync(async () =>
                await CallJs("renderVehicles", mapVehicles));
        }
        catch (Exception ex) { Trace.WriteLine($"[Realtime] Poll error: {ex.Message}"); }
    }

    private async void OnSimulateLocationClicked(object? sender, EventArgs e)
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
    // Trip detail
    // ────────────────────────────────────────────────────────────────────

    private async Task HandleTripDetailAsync(string tripId)
    {
        Trace.WriteLine($"[Trip] Detail request for '{tripId}'");

        // Find which operator this trip belongs to
        if (!_tripOperatorMap.TryGetValue(tripId, out var operatorId))
        {
            // Fallback: search all installed operators
            foreach (var op in _activeRealtimeOperators)
            {
                var map = _gtfsApi.GetTripRouteMapPublic(op.Id);
                if (map != null && map.ContainsKey(tripId)) { operatorId = op.Id; break; }
            }
            if (operatorId == 0)
            {
                Trace.WriteLine($"[Trip] Trip '{tripId}' not found in any operator");
                return;
            }
        }

        try
        {
            var stops = await _gtfsApi.ParseTripStopsAsync(operatorId, tripId);
            if (stops.Count == 0) { Trace.WriteLine($"[Trip] No stops found for '{tripId}'"); return; }

            // Get route info
            var routeId = string.Empty;
            var shortName = string.Empty;
            var headsign = string.Empty;
            var routeType = 3;
            var tripMap = _gtfsApi.GetTripRouteMapPublic(operatorId);
            if (tripMap != null && tripMap.TryGetValue(tripId, out var rid))
            {
                routeId = rid;
                routeType = _routeTypeMap.TryGetValue(rid, out var rt) ? rt : 3;
            }

            // Get vehicle position + realtime data
            VehiclePositionDto? vehicle = null;
            double vehicleLat = 0, vehicleLon = 0;
            bool isTracked = false;
            lock (_realtimeLock)
                _vehiclesByTrip.TryGetValue(tripId, out vehicle);

            if (vehicle != null)
            {
                vehicleLat = vehicle.Lat;
                vehicleLon = vehicle.Lon;
                isTracked = !vehicle.IsScheduledOnly;

                // Annotate each stop with delay from StopTimeUpdates
                if (vehicle.StopTimeUpdates != null)
                {
                    // Build stopId → STU map for fast lookup
                    var stuByStop = vehicle.StopTimeUpdates
                        .Where(u => u.StopId != null)
                        .ToDictionary(u => u.StopId!, u => u);

                    foreach (var stop in stops)
                    {
                        if (!stuByStop.TryGetValue(stop.StopId, out var stu)) continue;
                        int delaySec = stu.DelaySeconds;
                        stop.DelayMinutes = (int)Math.Round(delaySec / 60.0);
                        var sp = stop.ScheduledTime.Split(':');
                        if (sp.Length >= 2 && int.TryParse(sp[0], out int sh) && int.TryParse(sp[1], out int sm))
                        {
                            int estMins = sh * 60 + sm + stop.DelayMinutes.Value;
                            stop.EstimatedTime = $"{estMins / 60:D2}:{estMins % 60:D2}";
                        }
                    }
                }
            }

            // Mark passed stops (scheduled time < now - 1 min)
            int nowMins = DateTime.Now.Hour * 60 + DateTime.Now.Minute - 1;
            int currentIdx = 0;
            for (int i = 0; i < stops.Count; i++)
            {
                int t = TimeToMinutes(stops[i].ScheduledTime);
                if (t < nowMins) { stops[i].IsPassed = true; currentIdx = i + 1; }
                else break;
            }
            currentIdx = Math.Min(currentIdx, stops.Count - 1);

            // Build short name + headsign from last stop
            shortName = routeId; // fallback
            headsign = stops.Last().StopName;

            var detail = new TripDetailDto
            {
                TripId = tripId,
                RouteId = routeId,
                ShortName = shortName,
                Headsign = headsign,
                RouteType = routeType,
                Stops = stops,
                CurrentStopIndex = currentIdx,
                VehicleLat = vehicleLat,
                VehicleLon = vehicleLon,
                IsTracked = isTracked,
            };

            Trace.WriteLine($"[Trip] Sending {stops.Count} stops, currentIdx={currentIdx}");
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await CallJs("renderTripDetail", detail));
        }
        catch (Exception ex) { Trace.WriteLine($"[Trip] Exception: {ex.Message}"); }
    }

    private static int TimeToMinutes(string t)
    {
        var p = t.Split(':');
        return p.Length >= 2 && int.TryParse(p[0], out int h) && int.TryParse(p[1], out int m)
            ? h * 60 + m : 0;
    }

    // ────────────────────────────────────────────────────────────────────
    // JS bridge
    // ────────────────────────────────────────────────────────────────────

    private async Task CallJs(string fn, object data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        await MapWebView.EvaluateJavaScriptAsync($"{fn}(JSON.parse(atob('{base64}')))");
    }



}