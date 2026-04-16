using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using OpenTripPlannerAPI.Core;
using OpenTripPlannerAPI.Scrapers.Base;
using OpenTripPlannerAPI.Services;

namespace OpenTripPlannerAPI.Scrapers.Hzpp;

public partial class HzppScraper : IScraper
{
    private readonly ILogger<HzppScraper> _logger;
    private readonly HttpClient _client;
    private readonly HzppGtfsLoader _gtfsLoader;
    private readonly DbBackedOtpConfigState _state;
    private readonly IConfiguration _configuration;

    private static readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zagreb");
    private const string BaseUrl = "https://www.hzpp.app";

    private GtfsData? _gtfs;
    private readonly HzppScraperOptions _options;

    public HzppScraper(
        ILogger<HzppScraper> logger,
        IHttpClientFactory httpClientFactory,
        HzppGtfsLoader gtfsLoader,
        DbBackedOtpConfigState state,
        IConfiguration configuration)
    {
        _logger = logger;
        _client = httpClientFactory.CreateClient("hzpp");
        _gtfsLoader = gtfsLoader;
        _state = state;
        _configuration = configuration;
        _options = configuration.GetSection("Scrapers:Hzpp").Get<HzppScraperOptions>()
                   ?? configuration.GetSection("Scrapers:HZPP").Get<HzppScraperOptions>()
                   ?? new HzppScraperOptions();
    }

    public string FeedId => _options.FeedId;

    public bool IsEnabled => _options.Enabled;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds));

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (!IsEnabled)
            return;

        var gtfsUrl = _state.LocalScraperStaticGtfsUrls.TryGetValue(FeedId, out var localGtfsUrl)
            ? localGtfsUrl
            : _options.GtfsZipUrl;

        gtfsUrl = string.IsNullOrWhiteSpace(gtfsUrl)
            ? _configuration["Gtfs:ZipUrl"] ?? "https://www.hzpp.hr/GTFS_files.zip"
            : gtfsUrl;

        _logger.LogInformation("[{FeedId}] Loading GTFS from {Url}", FeedId, gtfsUrl);
        _gtfs = await _gtfsLoader.LoadAsync(gtfsUrl, ct);
    }

    public async Task<ScrapeResult?> ScrapeAsync(CancellationToken ct)
    {
        if (!IsEnabled || _gtfs is null)
            return null;

        var activeTrains = _gtfsLoader.GetActiveTrainNumbers(_gtfs);
        var updatesMap = new Dictionary<string, List<StopTimeUpdateDto>>();

        foreach (var trainNumber in activeTrains)
        {
            var payload = await FetchTrainDataAsync(trainNumber, ct);
            await Task.Delay(TimeSpan.FromSeconds(_options.RequestDelaySeconds), ct);

            if (payload is null)
                continue;

            if (payload.DelayMin == 0 && string.IsNullOrEmpty(payload.CurrentStation) && !payload.Finished)
                continue;

            var tripId = _gtfsLoader.GetActiveTripId(trainNumber, _gtfs);
            if (tripId is null)
                continue;

            var stus = ComputeStopTimeUpdates(tripId, payload, _gtfs);
            if (stus.Count > 0)
                updatesMap[tripId] = stus;
        }

        var bytes = ProtobufFeedBuilder.BuildFromStopTimeUpdates(updatesMap).ToByteArray();
        return new ScrapeResult { FeedBytes = bytes };
    }

    private async Task<TrainPayload?> FetchTrainDataAsync(string trainNumber, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/__data.json"
                + $"?trainId={trainNumber}"
                + "&x-sveltekit-trailing-slash=1"
                + "&x-sveltekit-invalidated=01";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _client.GetStringAsync(url, cts.Token);
            string? htmlContent = null;

            foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line.Trim());
                    var root = doc.RootElement;
                    if (root.TryGetProperty("type", out var t) && t.GetString() == "chunk"
                        && root.TryGetProperty("data", out var d))
                    {
                        foreach (var item in d.EnumerateArray())
                        {
                            var val = item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
                            if (val.Contains("<HTML>", StringComparison.OrdinalIgnoreCase))
                            {
                                htmlContent = val;
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // skip non-JSON lines
                }
            }

            if (htmlContent == null)
            {
                _logger.LogDebug("Train {Train}: no HTML chunk found", trainNumber);
                return null;
            }

            return ParseHzinfoHtml(htmlContent, trainNumber);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to fetch train {Train}: {Msg}", trainNumber, ex.Message);
            return null;
        }
    }

    private TrainPayload ParseHzinfoHtml(string html, string trainNumber)
    {
        var text = HttpUtility.HtmlDecode(html);
        var payload = new TrainPayload { TrainNumber = trainNumber };

        var m = StationRegex().Match(text);
        if (m.Success)
            payload.CurrentStation = m.Groups[1].Value.Trim().Replace("+", " ").Replace(".", " ").Trim();

        m = RouteRegex().Match(text);
        if (m.Success) payload.Route = m.Groups[1].Value.Trim();

        m = DelayRegex().Match(text);
        if (m.Success) payload.DelayMin = int.Parse(m.Groups[1].Value);

        payload.Finished = FinishedRegex().IsMatch(text);

        _logger.LogDebug("Train {Train}: station={Station} delay={Delay}min finished={Finished}",
            trainNumber, payload.CurrentStation, payload.DelayMin, payload.Finished);

        return payload;
    }

    public List<StopTimeUpdateDto> ComputeStopTimeUpdates(string tripId, TrainPayload train, GtfsData data)
    {
        if (!data.StopTimes.TryGetValue(tripId, out var stList) || stList.Count == 0)
            return [];

        var delaySec = train.DelayMin * 60;
        var currentStation = HzppGtfsLoader.Normalize(train.CurrentStation);
        var nowSec = (int)TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz).TimeOfDay.TotalSeconds;

        int? currentSeq = null;
        foreach (var st in stList)
        {
            var stopName = HzppGtfsLoader.Normalize(data.StopsById.GetValueOrDefault(st.StopId, ""));
            if (!string.IsNullOrEmpty(currentStation) && (
                stopName == currentStation ||
                currentStation.Contains(stopName, StringComparison.Ordinal) ||
                stopName.Contains(currentStation, StringComparison.Ordinal) ||
                currentStation.Split(' ').Any(w => w.Length > 3 && stopName.Contains(w, StringComparison.Ordinal))))
            {
                currentSeq = st.StopSequence;
                break;
            }
        }

        var updates = new List<StopTimeUpdateDto>();
        foreach (var st in stList)
        {
            if (currentSeq != null && st.StopSequence < currentSeq && !train.Finished)
                continue;

            var scheduledDepartureSec = st.DepartureSec >= 0 ? st.DepartureSec : st.ArrivalSec;
            if (currentSeq == null && scheduledDepartureSec <= nowSec)
                continue;

            updates.Add(new StopTimeUpdateDto
            {
                StopId = st.StopId,
                StopSequence = st.StopSequence,
                DelaySec = delaySec,
                ScheduledArrivalSec = st.ArrivalSec,
                ScheduledDepartureSec = st.DepartureSec
            });
        }

        return updates;
    }

    [GeneratedRegex(@"Kolodvor\s*:?\s*</I>\s*(?:<strong>)?([^<\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex StationRegex();

    [GeneratedRegex(@"Relacija\s*:?\s*(?:<br>)?\s*([^\r\n<]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RouteRegex();

    [GeneratedRegex(@"Kasni\s+(\d+)\s*min", RegexOptions.IgnoreCase)]
    private static partial Regex DelayRegex();

    [GeneratedRegex(@"Zavr[sš]io\s+vo[žz]nju", RegexOptions.IgnoreCase)]
    private static partial Regex FinishedRegex();
}
