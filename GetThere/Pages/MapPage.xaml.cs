using GetThere.Services;
using GetThere.State;
using GetThereShared.Dtos;
using System.Diagnostics;
using System.Text.Json;

namespace GetThere.Pages;

public partial class MapPage : ContentPage
{
    private readonly OperatorService _operatorService;
    private readonly CountryPreferenceService _countryPrefs;

    private System.Timers.Timer? _vehicleTimer;
    private System.Timers.Timer? _jsMessageTimer;

    private readonly TaskCompletionSource _navigatedTcs = new();

    public MapPage(OperatorService operatorService, CountryPreferenceService countryPrefs)
    {
        InitializeComponent();
        _operatorService = operatorService;
        _countryPrefs = countryPrefs;

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

    private async Task LoadHtmlAsync()
    {
        try
        {
            using var htmlStream = await FileSystem.OpenAppPackageFileAsync("map.html");
            using var htmlReader = new StreamReader(htmlStream);
            var html = await htmlReader.ReadToEndAsync();

            var apiBase = _operatorService.GetApiBaseUrl().TrimEnd('/');

            // 1. Inline map.css
            html = await InlineCssAsync(html);

            // 2. Inject API base URL
            html = html.Replace("</head>",
                $"<script>window._API_BASE = '{apiBase}';</script></head>",
                StringComparison.OrdinalIgnoreCase);

            // 3. Inject map style JSON as window._MAP_STYLE
            html = await InjectMapStyleAsync(html);

            // 4. Fetch transport types from server, inject icons + config
            html = await InjectTransportTypesAsync(html, apiBase);

            // 5. Inline map.js
            html = await InlineJsAsync(html);

            await MainThread.InvokeOnMainThreadAsync(() =>
                MapWebView.Source = new HtmlWebViewSource { Html = html });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] LoadHtml error: {ex.Message}");
        }
    }

    // Reads map.css and replaces the <link href="map.css"> tag with an inline <style> block.
    private async Task<string> InlineCssAsync(string html)
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("map.css");
            using var reader = new StreamReader(stream);
            var css = await reader.ReadToEndAsync();

            html = html.Replace(
                "<link href=\"map.css\" rel=\"stylesheet\" />",
                $"<style>{css}</style>",
                StringComparison.OrdinalIgnoreCase);

            Trace.WriteLine("[MapPage] map.css inlined.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] InlineCss error: {ex.Message}");
        }

        return html;
    }

    // Reads map.js and replaces the <script src="map.js"> tag with an inline <script> block.
    private async Task<string> InlineJsAsync(string html)
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("map.js");
            using var reader = new StreamReader(stream);
            var js = await reader.ReadToEndAsync();

            html = html.Replace(
                "<script src=\"map.js\"></script>",
                $"<script>{js}</script>",
                StringComparison.OrdinalIgnoreCase);

            Trace.WriteLine("[MapPage] map.js inlined.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] InlineJs error: {ex.Message}");
        }

        return html;
    }

    // Reads mapstyle.json and injects it as window._MAP_STYLE.
    private async Task<string> InjectMapStyleAsync(string html)
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("mapstyle.json");
            using var reader = new StreamReader(stream);
            var styleJson = await reader.ReadToEndAsync();

            var tilesConfig = await _operatorService.GetMapTilesConfigAsync();
            var safeTilesBase = string.Empty;
            if (Uri.TryCreate(tilesConfig?.TilesBaseUrl, UriKind.Absolute, out var parsed)
                && parsed.Scheme is "https" or "http")
            {
                safeTilesBase = parsed.ToString().TrimEnd('/');
            }

            var tilesBaseJson = JsonSerializer.Serialize(safeTilesBase);
            var apiKeyJson = JsonSerializer.Serialize(tilesConfig?.ApiKey?.Trim() ?? string.Empty);
            html = html.Replace("</head>",
                $"<script>window._MAP_STYLE = {styleJson};window._TL_TILES_BASE={tilesBaseJson};window._TL_API_KEY={apiKeyJson};</script></head>",
                StringComparison.OrdinalIgnoreCase);

            Trace.WriteLine("[MapPage] Map style injected.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] InjectMapStyle error: {ex.Message}");
        }

        return html;
    }

    // Fetches transport types from the API, injects window._TRANSPORT_TYPES
    // and pre-fetches each icon as base64 into window._ICON_DATA.
    private async Task<string> InjectTransportTypesAsync(string html, string apiBase)
    {
        try
        {
            var types = await _operatorService.GetTransportTypesAsync() ?? [];

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<script>");

            // Inject transport type config for map.js to build icon map dynamically
            var typesJson = JsonSerializer.Serialize(types,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            sb.AppendLine($"window._TRANSPORT_TYPES = {typesJson};");

            // Pre-fetch each icon as base64 to avoid CORS issues in WebView
            sb.AppendLine("window._ICON_DATA = {};");
            using var http = new HttpClient();

            // Deduplicate — multiple types can share the same icon file (e.g. tram + trolleybus)
            var uniqueFiles = types.Select(t => t.IconFile).Distinct();
            foreach (var file in uniqueFiles)
            {
                try
                {
                    var bytes = await http.GetByteArrayAsync($"{apiBase}/images/{file}");
                    var b64 = Convert.ToBase64String(bytes);
                    sb.AppendLine($"window._ICON_DATA['{file}'] = 'data:image/png;base64,{b64}';");
                    Trace.WriteLine($"[MapPage] Icon injected: {file} ({bytes.Length} bytes)");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[MapPage] Icon prefetch failed for {file}: {ex.Message}");
                }
            }

            sb.AppendLine("</script>");
            html = html.Replace("</head>", sb + "</head>", StringComparison.OrdinalIgnoreCase);

            Trace.WriteLine($"[MapPage] Transport types injected: {types.Count} types.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] InjectTransportTypes error: {ex.Message}");
        }

        return html;
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
            int? countryId = _countryPrefs.HasSelection ? _countryPrefs.GetSelectedCountryId() : null;

            await Task.WhenAll(
                _operatorService.GetMapFeaturesAsync(countryId).ContinueWith(async t =>
                {
                    if (t.Result is { } features)
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                            await CallJsAsync("renderMapFeatures", features));
                }),
                _operatorService.GetRoutesAsync(countryId).ContinueWith(async t =>
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
            int? countryId = _countryPrefs.HasSelection ? _countryPrefs.GetSelectedCountryId() : null;
            var vehicles = await _operatorService.GetVehiclesAsync(countryId);
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
