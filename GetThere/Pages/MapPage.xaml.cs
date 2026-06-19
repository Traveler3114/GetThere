using System.Diagnostics;
using System.Text.Json;

using GetThere.Services;
using GetThere.State;
using GetThereShared.Contracts;

namespace GetThere.Pages;

public partial class MapPage : ContentPage
{
    private readonly OperatorService _operatorService;
    private readonly CountryPreferenceService _countryPrefs;

    private System.Timers.Timer? _jsMessageTimer;
    private System.Timers.Timer? _vehicleTimer;

    private readonly TaskCompletionSource _navigatedTcs = new();
    private bool _isWebViewReady = false;

    public MapPage(OperatorService operatorService, CountryPreferenceService countryPrefs)
    {
        InitializeComponent();
        _operatorService = operatorService;
        _countryPrefs = countryPrefs;

        MapWebView.Navigated += OnWebViewNavigated;
        _ = LoadHtmlAsync();
    }

    private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result == WebNavigationResult.Success)
        {
            _navigatedTcs.TrySetResult();
            _isWebViewReady = true;
        }
    }

    private string GetApiBaseUrl()
    {
#if ANDROID
        return "https://10.0.2.2:7230";
#else
        return "https://localhost:7230";
#endif
    }

    private async Task LoadHtmlAsync()
    {
        try
        {
            using var htmlStream = await FileSystem.OpenAppPackageFileAsync("map.html");
            using var htmlReader = new StreamReader(htmlStream);
            var html = await htmlReader.ReadToEndAsync();

            html = await InlineCssAsync(html);

            var apiBase = GetApiBaseUrl();
            html = html.Replace("</head>",
                $"<script>window._API_BASE = '{apiBase}';</script></head>",
                StringComparison.OrdinalIgnoreCase);

            html = await InjectTransportTypesAsync(html);

            html = await InlineJsAsync(html);

            await MainThread.InvokeOnMainThreadAsync(() =>
                MapWebView.Source = new HtmlWebViewSource { Html = html });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] LoadHtml error: {ex.Message}");
        }
    }

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

    private async Task<string> InjectTransportTypesAsync(string html)
    {
        try
        {
            var typesResult = await _operatorService.GetTransportTypesAsync();
            var types = typesResult.Success && typesResult.Data is not null ? typesResult.Data : [];

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<script>");

            var typesJson = JsonSerializer.Serialize(types,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            sb.AppendLine($"window._TRANSPORT_TYPES = {typesJson};");

            sb.AppendLine("window._ICON_DATA = {};");
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
            };
            using var http = new HttpClient(handler);
            var apiBase = GetApiBaseUrl();

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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _navigatedTcs.Task;
        await WaitForMapReadyAsync();
        _isWebViewReady = true;
        await LoadStaticDataAsync();
        await GetLocationAsync();
        StartJsMessagePolling();
        StartVehiclePolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopJsMessagePolling();
        StopVehiclePolling();
    }

    private void StartVehiclePolling()
    {
        _vehicleTimer = new System.Timers.Timer(15000);
        _vehicleTimer.Elapsed += async (_, _) => await FetchVehiclesAsync();
        _vehicleTimer.AutoReset = true;
        _vehicleTimer.Start();
    }

    private void StopVehiclePolling()
    {
        _vehicleTimer?.Stop();
        _vehicleTimer?.Dispose();
        _vehicleTimer = null;
    }

    private async Task FetchVehiclesAsync()
    {
        if (!_isWebViewReady) return;
        try
        {
            var result = await _operatorService.GetVehiclesAsync();
            if (result.Success && result.Data is not null)
            {
                var mapped = result.Data.Select(v => new
                {
                    vehicleId = v.VehicleId,
                    routeId = v.RouteId,
                    tripId = v.TripId,
                    routeShortName = v.RouteShortName,
                    isRealtime = v.IsRealtime,
                    bearing = v.Bearing,
                    blockId = v.BlockId,
                    lat = v.Latitude,
                    lon = v.Longitude
                }).ToList();

                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await CallJsAsync("renderVehicles", mapped));
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] FetchVehicles error: {ex.Message}");
        }
    }

    private async Task WaitForMapReadyAsync()
    {
        while (true)
        {
            try
            {
                string? result = null;
                try
                {
                    result = await MainThread.InvokeOnMainThreadAsync(async () =>
                        await MapWebView.EvaluateJavaScriptAsync(
                            "window._mapReady === true ? '1' : (window._jsError || '0')"));
                }
                catch (InvalidOperationException)
                {
                    await Task.Delay(300);
                    continue;
                }

                var clean = result?.Trim('"', '\'', ' ') ?? "0";
                if (clean == "1") break;

                if (clean.StartsWith("JS ERROR:") || clean.StartsWith("onMapLoad ERROR:"))
                    Trace.WriteLine($"[MapPage] {clean}");
            }
            catch { }

            await Task.Delay(300);
        }

        Trace.WriteLine("[MapPage] Map ready");
    }

    private async Task LoadStaticDataAsync()
    {
        try
        {
            int? countryId = _countryPrefs.HasSelection ? _countryPrefs.GetSelectedCountryId() : null;

            var stopsTask = _operatorService.GetStopsAsync(countryId);
            var routesTask = _operatorService.GetRoutesAsync(countryId);
            var stationsTask = _operatorService.GetBikeStationsAsync(countryId);

            await Task.WhenAll(stopsTask, routesTask, stationsTask);

            var stops = await stopsTask;
            if (stops.Success && stops.Data is not null)
            {
                var mapped = stops.Data.Select(s => new
                {
                    stopId = s.GlobalId,
                    name = s.Name,
                    lat = s.Latitude,
                    lon = s.Longitude,
                    stationType = s.StationType ?? "Stop",
                    routeType = _StationTypeToRouteType(s.StationType)
                }).ToList();

                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await CallJsAsync("renderStops", mapped));
            }

            var routes = await routesTask;
            if (routes.Success && routes.Data is not null)
            {
                var mapped = routes.Data.Select(r => new
                {
                    routeId = r.GlobalId,
                    name = r.Name,
                    routeType = r.RouteType
                }).ToList();

                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await CallJsAsync("renderRoutes", mapped));
            }

            var stations = await stationsTask;
            if (stations.Success && stations.Data is not null)
            {
                Trace.WriteLine($"[MapPage] Bike stations loaded: {stations.Data.Count}");
                var mapped = stations.Data.Select(s => new
                {
                    stationId = s.StationId,
                    name = s.Name,
                    lon = s.Longitude,
                    lat = s.Latitude,
                    availableBikes = s.AvailableVehicles,
                    capacity = s.Capacity,
                    providerName = s.ProviderName
                }).ToList();

                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await CallJsAsync("renderBikeStations", mapped));
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] LoadStaticData error: {ex.Message}");
        }
    }

    private static int _StationTypeToRouteType(string? stationType)
    {
        return stationType?.ToLowerInvariant() switch
        {
            "tram" => 0,
            "metro" => 1,
            "rail" or "train" => 2,
            "bus" => 3,
            "ferry" => 4,
            "trolleybus" => 11,
            _ => 3
        };
    }

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

            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await MapWebView.EvaluateJavaScriptAsync($"updateMapLocation({lon},{lat})"));
            }
            catch (InvalidOperationException)
            {
                Trace.WriteLine("[MapPage] Location JS update skipped: WebView not ready.");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] Location error: {ex.Message}");
        }
    }

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
        if (!_isWebViewReady) return;

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
        }
        catch (InvalidOperationException ex)
        {
            Trace.WriteLine($"[MapPage] PollJsMessages skipped: {ex.Message}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] PollJsMessages error: {ex.Message}");
        }
    }

    private async Task HandleStopTappedAsync(string globalId)
    {
        var departuresTask = _operatorService.GetStationDeparturesAsync(globalId);
        var operatorsTask = _operatorService.GetStationOperatorsAsync(globalId);

        await Task.WhenAll(departuresTask, operatorsTask);

        var departures = await departuresTask;
        var operators = await operatorsTask;

        var data = new
        {
            departures = departures.Success && departures.Data is not null
                ? departures.Data
                : new List<MapDepartureResponse>(),
            operators = operators.Success && operators.Data is not null
                ? operators.Data
                : new List<MapOperatorResponse>()
        };

        await MainThread.InvokeOnMainThreadAsync(async () =>
            await CallJsAsync("renderStopSchedule", data));
    }

    private async Task CallJsAsync(string function, object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            try
            {
                await MapWebView.EvaluateJavaScriptAsync(
                    $"{function}(JSON.parse(atob('{b64}')))");
            }
            catch (InvalidOperationException ex)
            {
                Trace.WriteLine($"[MapPage] CallJs({function}) skipped: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapPage] CallJs({function}) error: {ex.Message}");
        }
    }
}
