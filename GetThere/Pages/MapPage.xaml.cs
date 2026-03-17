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

    // Completes once WebView2 finishes navigating — before this,
    // EvaluateJavaScriptAsync returns null on Windows.
    private readonly TaskCompletionSource _navigatedTcs = new();

    public MapPage(OperatorService operatorService)
    {
        InitializeComponent();
        _operatorService = operatorService;

        // Hook before setting Source so the event is never missed.
        MapWebView.Navigated += OnWebViewNavigated;
        _ = LoadHtmlAsync();
    }

    // ── WebView navigation ────────────────────────────────────────────────

    private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result == WebNavigationResult.Success)
            _navigatedTcs.TrySetResult();
    }

    // ── Load HTML ─────────────────────────────────────────────────────────
    // ── Load HTML ─────────────────────────────────────────────────────────
    // Injects window._API_BASE so the JS icon loader can build absolute URLs.
    // The style JSON is already baked into index.html at build time (no injection needed).

    private async Task LoadHtmlAsync()
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("index.html");
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();

            // Inject the API base URL so JS can load icons via GET /operator/images/*.png
            var apiBase = _operatorService.GetApiBaseUrl().TrimEnd('/');
            var injection = $"<script>window._API_BASE = '{apiBase}';</script>";
            html = html.Replace("</head>", injection + "</head>",
                StringComparison.OrdinalIgnoreCase);

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
        await _navigatedTcs.Task;
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
        while (true)
        {
            try
            {
                var result = await MainThread.InvokeOnMainThreadAsync(async () =>
                    await MapWebView.EvaluateJavaScriptAsync(
                        "window._mapReady === true ? '1' : (window._jsError || '0')"));

                var clean = result?.Trim('"', '\'', ' ') ?? "0";
                if (clean == "1") break;

                if (clean.StartsWith("JS ERROR:") || clean.StartsWith("onMapLoad ERROR:"))
                    Trace.WriteLine($"[MapPage] {clean}");
            }
            catch { /* WebView not ready yet */ }

            await Task.Delay(300);
        }

        Trace.WriteLine("[MapPage] Map ready");
    }

    // ── Static data ───────────────────────────────────────────────────────

    private async Task LoadStaticDataAsync()
    {
        try
        {
            await Task.WhenAll(
                _operatorService.GetStopsAsync().ContinueWith(async t =>
                {
                    if (t.Result is { } stops)
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                            await CallJsAsync("renderStops", stops));
                }),
                _operatorService.GetRoutesAsync().ContinueWith(async t =>
                {
                    if (t.Result is { } routes)
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                            await CallJsAsync("renderRoutes", routes));
                })
            );
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

            var lon = location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var lat = location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);

            await MainThread.InvokeOnMainThreadAsync(async () =>
                await MapWebView.EvaluateJavaScriptAsync($"updateMapLocation({lon},{lat})"));
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
        Task.Run(PollVehiclesAsync);   // immediate first fetch
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

            await MainThread.InvokeOnMainThreadAsync(async () =>
                await CallJsAsync("renderVehicles", vehicles));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] PollVehicles error: {ex.Message}");
        }
    }

    // ── JS → C# message polling ───────────────────────────────────────────

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
                    "(function(){ var m=window._pendingMsg||''; window._pendingMsg=''; return m; })()"));

            if (string.IsNullOrEmpty(raw) || raw == "null") return;

            // Strip outer quotes added by some WebView implementations
            var msg = raw.Trim();
            if (msg.Length >= 2 && msg[0] == '"' && msg[^1] == '"')
                msg = msg[1..^1];
            if (string.IsNullOrEmpty(msg)) return;

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
        var schedule = await _operatorService.GetStopScheduleAsync(stopId);

        await MainThread.InvokeOnMainThreadAsync(async () =>
            await CallJsAsync("renderStopSchedule",
                schedule ?? (object)new { stopId, groups = Array.Empty<object>() }));
    }

    private async Task HandleVehicleTappedAsync(string tripId)
    {
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
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            await MapWebView.EvaluateJavaScriptAsync(
                $"{function}(JSON.parse(atob('{b64}')))");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] CallJs({function}) error: {ex.Message}");
        }
    }
}
