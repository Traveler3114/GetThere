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
        catch (Exception ex)
        {
            Trace.WriteLine($"Location error: {ex.Message}");
        }
        finally
        {
            LoadingOverlay.IsVisible = false;
        }
    }

    private async Task LoadGtfsAsync()
    {
        try
        {
            var operators = await _operatorService.GetAllAsync();

            _activeRealtimeOperatorIds.Clear();
            var allStops = new List<object>();
            var allRoutes = new List<object>();

            foreach (var op in operators)
            {
                // Only render if locally installed by user
                if (!_gtfsApi.IsInstalled(op.Id)) continue;

                if (op.IsScheduleEnabled)
                {
                    var stops = await _gtfsApi.ParseStopsAsync(op.Id);
                    var routes = await _gtfsApi.ParseRoutesAsync(op.Id);

                    allStops.AddRange(stops.Select(s => new
                    {
                        stopId = s.StopId,
                        name = s.Name,
                        lat = s.Lat,
                        lon = s.Lon
                    }));

                    allRoutes.AddRange(routes.Select(r => new
                    {
                        routeId = r.RouteId,
                        shortName = r.ShortName,
                        color = r.Color,
                        shape = r.Shape
                    }));
                }

                // Show real-time only if schedule package is installed for this operator
                if (op.IsRealtimeEnabled && op.IsScheduleEnabled && _gtfsApi.IsInstalled(op.Id))
                    _activeRealtimeOperatorIds.Add(op.Id);

                /* Uncomment below for "show realtime even if not installed"
                // if (op.IsRealtimeEnabled) _activeRealtimeOperatorIds.Add(op.Id);
                */
            }

            await CallJs("renderStops", allStops);
            await CallJs("renderRoutes", allRoutes);

        }
        catch (Exception ex)
        {
            Trace.WriteLine($"GTFS load error: {ex.Message}");
        }
    }

    private void StartRealtimePolling()
    {
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
            var allVehicles = new List<object>();

            foreach (var opId in _activeRealtimeOperatorIds)
            {
                var vehicles = await _gtfsApi.GetVehiclesAsync(opId);
                allVehicles.AddRange(vehicles.Select(v => new
                {
                    vehicleId = v.VehicleId,
                    routeId = v.RouteId,
                    lat = v.Lat,
                    lon = v.Lon,
                    bearing = v.Bearing,
                    label = v.Label
                }));
            }

            await MainThread.InvokeOnMainThreadAsync(async () =>
                await CallJs("renderVehicles", allVehicles));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Realtime poll error: {ex.Message}");
        }
    }

    private async Task CallJs(string fn, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var escaped = json.Replace("\\", "\\\\").Replace("'", "\\'");
        await MapWebView.EvaluateJavaScriptAsync($"{fn}('{escaped}')");
    }
}