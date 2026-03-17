using GetThere.Services;
using GetThereShared.Dtos;
using System.Diagnostics;
using System.Text.Json;

namespace GetThere.Pages;

public partial class MapPage : ContentPage
{
    private readonly OperatorService _operatorService;

    private System.Timers.Timer? _vehicleTimer;
    private System.Timers.Timer? _jsMessageTimer;

    // Signals that WebView2 navigation is complete and JS calls will work.
    private readonly TaskCompletionSource _navigatedTcs = new();

    public MapPage(OperatorService operatorService)
    {
        InitializeComponent();
        _operatorService = operatorService;

        MapWebView.Navigated += OnWebViewNavigated;
        _ = LoadHtmlAsync();
    }

    // ── WebView navigation ────────────────────────────────────────────────

    private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        Trace.WriteLine($"[MapPage] Navigated: result={e.Result} url={e.Url}");
        if (e.Result == WebNavigationResult.Success)
            _navigatedTcs.TrySetResult();
    }

    // ── Load HTML ─────────────────────────────────────────────────────────
    // index.html already contains the map style hardcoded — no injection needed.

    private async Task LoadHtmlAsync()
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("index.html");
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();

            Trace.WriteLine($"[MapPage] HTML loaded, length={html.Length}");

            await MainThread.InvokeOnMainThreadAsync(() =>
                MapWebView.Source = new HtmlWebViewSource { Html = html });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] LoadHtml error: {ex.Message}");
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        Trace.WriteLine("[MapPage] Waiting for WebView navigation...");
        await _navigatedTcs.Task;
        Trace.WriteLine("[MapPage] Navigation done, polling map ready...");

        await WaitForMapReadyAsync();
        await LoadStaticDataAsync();
        await GetLocationAsync();
        StartVehiclePolling();
        StartJsMessagePolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopVehiclePolling();
        StopJsMessagePolling();
    }

    // ── Map ready handshake ───────────────────────────────────────────────

    private async Task WaitForMapReadyAsync()
    {
        var attempts = 0;
        while (true)
        {
            try
            {
                // Also read any JS error so we can log it
                var ready = await MainThread.InvokeOnMainThreadAsync(async () =>
                    await MapWebView.EvaluateJavaScriptAsync(
                        "window._mapReady === true ? '1' : (window._jsError || '0')"));

                attempts++;
                var clean = ready?.Trim('"', '\'', ' ') ?? "";
                Trace.WriteLine($"[MapPage] poll #{attempts}: '{clean}'");

                if (clean == "1") break;

                // If we got an actual error message back, log it loudly
                if (clean.StartsWith("ERROR:"))
                {
                    Trace.WriteLine($"[MapPage] JS CRASH DETECTED: {clean}");
                    // Still keep polling — don't give up, maybe it recovers
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MapPage] poll error: {ex.Message}");
            }

            await Task.Delay(300);
        }

        Trace.WriteLine("[MapPage] Map is ready!");
    }

    // ── Static data ───────────────────────────────────────────────────────

    private async Task LoadStaticDataAsync()
    {
        try
        {
            var stopsTask = _operatorService.GetStopsAsync();
            var routesTask = _operatorService.GetRoutesAsync();
            await Task.WhenAll(stopsTask, routesTask);

            var stops = stopsTask.Result;
            var routes = routesTask.Result;

            if (stops is not null)
            {
                Trace.WriteLine($"[MapPage] Sending {stops.Count} stops");
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await CallJsAsync("renderStops", stops));
            }
            else Trace.WriteLine("[MapPage] stops == null");

            if (routes is not null)
            {
                Trace.WriteLine($"[MapPage] Sending {routes.Count} routes");
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await CallJsAsync("renderRoutes", routes));
            }
            else Trace.WriteLine("[MapPage] routes == null");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] LoadStaticData error: {ex.Message}");
        }
    }

    // ── User location ─────────────────────────────────────────────────────

    private async Task GetLocationAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted) return;

            var location = await Geolocation.GetLocationAsync(new GeolocationRequest
            {
                DesiredAccuracy = GeolocationAccuracy.Best,
                Timeout = TimeSpan.FromSeconds(10)
            });
            if (location is null) return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
                await MapWebView.EvaluateJavaScriptAsync(
                    $"updateMapLocation(" +
                    $"{location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    $"{location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)})"));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] Location error: {ex.Message}");
        }
    }

    // ── Vehicle polling ───────────────────────────────────────────────────

    private void StartVehiclePolling()
    {
        _vehicleTimer = new System.Timers.Timer(10_000);
        _vehicleTimer.Elapsed += async (_, _) => await PollVehiclesAsync();
        _vehicleTimer.AutoReset = true;
        _vehicleTimer.Start();
        Task.Run(PollVehiclesAsync);
    }

    private void StopVehiclePolling()
    {
        _vehicleTimer?.Stop();
        _vehicleTimer?.Dispose();
        _vehicleTimer = null;
    }

    private async Task PollVehiclesAsync()
    {
        try
        {
            var vehicles = await _operatorService.GetVehiclesAsync();
            if (vehicles is null) return;
            Trace.WriteLine($"[MapPage] {vehicles.Count} vehicles");
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await CallJsAsync("renderVehicles", vehicles));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] PollVehicles error: {ex.Message}");
        }
    }

    // ── JS message polling ────────────────────────────────────────────────

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
                raw = await MapWebView.EvaluateJavaScriptAsync(
                    "(function(){ var m = window._pendingMsg || ''; window._pendingMsg = ''; return m; })()"));

            if (string.IsNullOrEmpty(raw) || raw == "null") return;

            var msg = raw.Trim();
            if (msg.Length >= 2 && msg[0] == '"' && msg[^1] == '"')
                msg = msg[1..^1];
            if (string.IsNullOrEmpty(msg)) return;

            Trace.WriteLine($"[MapPage] JS message: {msg}");

            if (msg.StartsWith("stopSchedule:", StringComparison.Ordinal))
                await HandleStopTappedAsync(msg["stopSchedule:".Length..]);
            else if (msg.StartsWith("tripDetail:", StringComparison.Ordinal))
                await HandleVehicleTappedAsync(msg["tripDetail:".Length..]);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] PollJsMessages error: {ex.Message}");
        }
    }

    // ── Message handlers ──────────────────────────────────────────────────

    private async Task HandleStopTappedAsync(string stopId)
    {
        Trace.WriteLine($"[MapPage] Stop tapped: {stopId}");
        var schedule = await _operatorService.GetStopScheduleAsync(stopId);
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (schedule is null)
                await CallJsAsync("renderStopSchedule", new { stopId, groups = Array.Empty<object>() });
            else
                await CallJsAsync("renderStopSchedule", schedule);
        });
    }

    private async Task HandleVehicleTappedAsync(string tripId)
    {
        Trace.WriteLine($"[MapPage] Vehicle tapped: {tripId}");
        var detail = await _operatorService.GetTripDetailAsync(tripId);
        if (detail is null) return;
        await MainThread.InvokeOnMainThreadAsync(async () =>
            await CallJsAsync("renderTripDetail", detail));
    }

    // ── JS bridge ─────────────────────────────────────────────────────────

    private async Task CallJsAsync(string function, object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            var result = await MapWebView.EvaluateJavaScriptAsync(
                $"{function}(JSON.parse(atob('{base64}')))");
            Trace.WriteLine($"[MapPage] CallJs({function}) => '{result}'");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] CallJs({function}) error: {ex.Message}");
        }
    }
}