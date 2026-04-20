using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using OpenTripPlannerAPI.Core;
using OpenTripPlannerAPI.Scrapers.Base;
using OpenTripPlannerAPI.Services;

namespace OpenTripPlannerAPI.Scrapers.Hzpp;

public partial class HzppScraper : ScraperBase
{
    private readonly ILogger<HzppScraper> _logger;
    private readonly HttpClient _client;
    private readonly HzppGtfsLoader _gtfsLoader;
    private readonly IConfiguration _config;
    private readonly DbBackedOtpConfigState _state;
    private GtfsData? _gtfsData;

    private static readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zagreb");
    private const string BaseUrl = "https://www.hzpp.app";

    public HzppScraper(
        ILogger<HzppScraper> logger,
        IHttpClientFactory httpClientFactory,
        HzppGtfsLoader gtfsLoader,
        IConfiguration config,
        DbBackedOtpConfigState state,
        ProtobufFeedBuilder feedBuilder)
        : base(feedBuilder)
    {
        _logger = logger;
        _client = httpClientFactory.CreateClient("hzpp");
        _gtfsLoader = gtfsLoader;
        _config = config;
        _state = state;
    }

    public override string FeedId => "hzpp";
    public override bool IsEnabled => _state.UsesLocalHzppScraper;

    public override async Task InitialiseAsync(CancellationToken ct)
    {
        if (!IsEnabled)
        {
            _logger.LogInformation("HZPP scraper is disabled by DB config.");
            return;
        }

        var gtfsUrl = _state.LocalHzppStaticGtfsUrl
                      ?? _config["Gtfs:ZipUrl"]
                      ?? "https://www.hzpp.hr/GTFS_files.zip";

        _logger.LogInformation("Loading HZPP GTFS from {Url}", gtfsUrl);
        _gtfsData = await _gtfsLoader.LoadAsync(gtfsUrl, ct);
    }

    public override async Task<ScrapeResult> ScrapeAsync(CancellationToken ct)
    {
        if (!IsEnabled || _gtfsData is null)
            return BuildEmptyResult();

        var requestDelay = double.TryParse(_config["Scrape:RequestDelaySeconds"], out var parsedRequestDelay) && parsedRequestDelay >= 0
            ? parsedRequestDelay
            : 0.3;
        var activeTrains = _gtfsLoader.GetActiveTrainNumbers(_gtfsData);

        var total = activeTrains.Count;
        var processed = 0;
        var withUpdates = 0;
        var updatesMap = new Dictionary<string, List<StopTimeUpdateData>>();
        var serviceDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz));
        var serviceDateGtfs = serviceDate.ToString("yyyyMMdd");

        foreach (var tnum in activeTrains)
        {
            processed++;
            Console.Write($"\r⏳ [{FeedId}] Scraping trains... {processed}/{total}");

            var payload = await FetchTrainDataAsync(tnum, ct);
            await Task.Delay(TimeSpan.FromSeconds(requestDelay), ct);

            if (payload is null)
                continue;

            if (payload.DelayMin == 0 && string.IsNullOrEmpty(payload.CurrentStation) && !payload.Finished)
                continue;

            var tripId = _gtfsLoader.GetActiveTripId(tnum, _gtfsData, serviceDate);
            if (tripId is null)
            {
                _logger.LogDebug("No active trip for train {Train} today", tnum);
                continue;
            }

            var stus = ComputeStopTimeUpdates(tripId, payload, _gtfsData, serviceDateGtfs);
            if (stus.Count > 0)
            {
                updatesMap[tripId] = stus;
                withUpdates++;
            }
        }

        Console.WriteLine($"\r✅ [{FeedId}] Scrape done — {withUpdates}/{total} trains with updates at {DateTime.Now:HH:mm:ss}");

        return BuildResult(updatesMap, processed, total, withUpdates);
    }

    private async Task<TrainPayload?> FetchTrainDataAsync(string trainNumber, CancellationToken ct)
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
                            var val = item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty;
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
                    // Ignore non-JSON lines.
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
        if (m.Success)
            payload.Route = m.Groups[1].Value.Trim();

        m = DelayRegex().Match(text);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var delayMin))
            payload.DelayMin = delayMin;

        payload.Finished = FinishedRegex().IsMatch(text);

        _logger.LogDebug("Train {Train}: station={Station} delay={Delay}min finished={Finished}",
            trainNumber, payload.CurrentStation, payload.DelayMin, payload.Finished);

        return payload;
    }

    private List<StopTimeUpdateData> ComputeStopTimeUpdates(string tripId, TrainPayload train, GtfsData data, string serviceDateGtfs)
    {
        if (!data.StopTimes.TryGetValue(tripId, out var stList) || stList.Count == 0)
            return [];

        var delaySec = train.DelayMin * 60;
        var currentStation = HzppGtfsLoader.Normalize(train.CurrentStation);
        var nowSec = (int)TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz).TimeOfDay.TotalSeconds;

        int? currentSeq = null;
        foreach (var st in stList)
        {
            var stopName = HzppGtfsLoader.Normalize(data.StopsById.GetValueOrDefault(st.StopId, string.Empty));
            if (!string.IsNullOrEmpty(currentStation) &&
                (stopName == currentStation
                 || currentStation.Contains(stopName, StringComparison.Ordinal)
                 || stopName.Contains(currentStation, StringComparison.Ordinal)
                 || currentStation.Split(' ').Any(w => w.Length > 3 && stopName.Contains(w, StringComparison.Ordinal))))
            {
                currentSeq = st.StopSequence;
                break;
            }
        }

        var updates = new List<StopTimeUpdateData>();
        foreach (var st in stList)
        {
            if (currentSeq != null && st.StopSequence < currentSeq && !train.Finished)
                continue;

            var scheduledDepartureSec = st.DepartureSec >= 0 ? st.DepartureSec : st.ArrivalSec;
            if (currentSeq == null && scheduledDepartureSec <= nowSec)
                continue;

            updates.Add(new StopTimeUpdateData
            {
                StopId = st.StopId,
                StopSequence = st.StopSequence,
                DelaySec = delaySec,
                ScheduledArrivalSec = st.ArrivalSec,
                ScheduledDepartureSec = st.DepartureSec,
                TripStartDate = serviceDateGtfs
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
