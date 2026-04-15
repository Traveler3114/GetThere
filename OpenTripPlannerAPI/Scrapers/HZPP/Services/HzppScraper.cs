using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using OpenTripPlannerAPI.Scrapers.HZPP.Models;

namespace OpenTripPlannerAPI.Scrapers.HZPP.Services;

public partial class HzppScraper
{
    private readonly ILogger<HzppScraper> _logger;
    private readonly HttpClient _client;
    private static readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zagreb");

    private const string BaseUrl = "https://www.hzpp.app";

    public HzppScraper(ILogger<HzppScraper> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _client = httpClientFactory.CreateClient("hzpp");
    }

    public async Task<TrainPayload?> FetchTrainDataAsync(string trainNumber, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/__data.json"
                + $"?trainId={trainNumber}"
                + $"&x-sveltekit-trailing-slash=1"
                + $"&x-sveltekit-invalidated=01";

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
                        foreach (var item in d.EnumerateArray())
                        {
                            var val = item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
                            if (val.Contains("<HTML>", StringComparison.OrdinalIgnoreCase))
                            { htmlContent = val; break; }
                        }
                }
                catch { /* skip non-JSON lines */ }
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
        var currentStation = GtfsLoader.Normalize(train.CurrentStation);
        var nowSec = (int)TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz).TimeOfDay.TotalSeconds;

        int? currentSeq = null;
        foreach (var st in stList)
        {
            var stopName = GtfsLoader.Normalize(data.StopsById.GetValueOrDefault(st.StopId, ""));
            if (!string.IsNullOrEmpty(currentStation) && (
                stopName == currentStation ||
                currentStation.Contains(stopName, StringComparison.Ordinal) ||
                stopName.Contains(currentStation, StringComparison.Ordinal) ||
                currentStation.Split(' ').Any(w => w.Length > 3 && stopName.Contains(w, StringComparison.Ordinal))))
            { currentSeq = st.StopSequence; break; }
        }

        var updates = new List<StopTimeUpdateDto>();
        foreach (var st in stList)
        {
            // If we know the current station, only update from there onward
            if (currentSeq != null && st.StopSequence < currentSeq && !train.Finished)
                continue;

            // If we don't know current station, never emit already-departed stops
            var scheduledDepartureSec = st.DepartureSec > 0 ? st.DepartureSec : st.ArrivalSec;
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
