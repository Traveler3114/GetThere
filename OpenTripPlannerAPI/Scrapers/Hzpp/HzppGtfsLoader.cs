using System.Globalization;
using System.IO.Compression;
using CsvHelper;
using CsvHelper.Configuration;

namespace OpenTripPlannerAPI.Scrapers.Hzpp;

public sealed class HzppGtfsLoader
{
    private readonly ILogger<HzppGtfsLoader> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zagreb");

    public HzppGtfsLoader(ILogger<HzppGtfsLoader> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<GtfsData> LoadAsync(string gtfsZipUrl, CancellationToken ct = default)
    {
        _logger.LogInformation("Downloading GTFS zip from {Url}", gtfsZipUrl);
        var client = _httpClientFactory.CreateClient("gtfs");
        var bytes = await client.GetByteArrayAsync(gtfsZipUrl, ct);
        _logger.LogInformation("Downloaded {Kb:F1} KB", bytes.Length / 1024.0);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var data = new GtfsData();

        LoadStops(zip, data);
        LoadTrips(zip, data);
        LoadStopTimes(zip, data);
        LoadCalendar(zip, data);

        _logger.LogInformation(
            "Loaded {Stops} stops, {Trips} trips, {StopTimes} stop-time entries",
            data.StopsById.Count,
            data.TripsById.Count,
            data.StopTimes.Values.Sum(v => v.Count));

        return data;
    }

    public string? GetActiveTripId(string trainNumber, GtfsData data, DateOnly? forDate = null)
    {
        var date = forDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz));
        if (!data.TripsByTrain.TryGetValue(trainNumber, out var candidates))
            return null;

        foreach (var tid in candidates)
        {
            var svc = data.TripsById[tid].ServiceId;
            if (data.Calendar.TryGetValue(svc, out var dates) && dates.Contains(date))
                return tid;
        }

        return candidates.Count > 0 ? candidates[0] : null;
    }

    public List<string> GetActiveTrainNumbers(GtfsData data)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz));
        var active = new HashSet<string>();

        foreach (var (tnum, tids) in data.TripsByTrain)
        {
            foreach (var tid in tids)
            {
                var svc = data.TripsById[tid].ServiceId;
                if (data.Calendar.TryGetValue(svc, out var dates) && dates.Contains(today))
                {
                    active.Add(tnum);
                    break;
                }
            }
        }

        if (active.Count == 0)
            active = data.TripsByTrain.Keys.ToHashSet();

        return [.. active.OrderBy(x => int.TryParse(x, out var n) ? n : int.MaxValue)];
    }

    public static string Normalize(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Trim().ToLowerInvariant();

    private void LoadStops(ZipArchive zip, GtfsData data)
    {
        using var reader = OpenEntry(zip, "stops.txt");
        using var csv = new CsvReader(reader, CsvConfig());
        foreach (var row in csv.GetRecords<dynamic>())
        {
            var record = (IDictionary<string, object>)row;
            var sid = record["stop_id"].ToString()!.Trim();
            var name = record["stop_name"].ToString()!.Trim();
            data.StopsById[sid] = name;
            data.StopIdByName[Normalize(name)] = sid;
        }
    }

    private void LoadTrips(ZipArchive zip, GtfsData data)
    {
        using var reader = OpenEntry(zip, "trips.txt");
        using var csv = new CsvReader(reader, CsvConfig());
        foreach (var row in csv.GetRecords<dynamic>())
        {
            var record = (IDictionary<string, object>)row;
            var tid = record["trip_id"].ToString()!.Trim();
            var svc = record["service_id"].ToString()!.Trim();
            var tnum = record.ContainsKey("trip_short_name") ? record["trip_short_name"].ToString()!.Trim() : string.Empty;
            var info = new TripInfo { TripId = tid, ServiceId = svc, TrainNumber = tnum };
            data.TripsById[tid] = info;
            if (!string.IsNullOrEmpty(tnum))
            {
                if (!data.TripsByTrain.TryGetValue(tnum, out var list))
                    data.TripsByTrain[tnum] = list = [];
                list.Add(tid);
            }
        }
    }

    private void LoadStopTimes(ZipArchive zip, GtfsData data)
    {
        using var reader = OpenEntry(zip, "stop_times.txt");
        using var csv = new CsvReader(reader, CsvConfig());
        foreach (var row in csv.GetRecords<dynamic>())
        {
            var record = (IDictionary<string, object>)row;
            var tid = record["trip_id"].ToString()!.Trim();
            var sid = record["stop_id"].ToString()!.Trim();
            if (!int.TryParse(record["stop_sequence"].ToString(), out var seq))
            {
                _logger.LogDebug("Skipping stop_time with invalid stop_sequence for trip {TripId}", tid);
                continue;
            }
            var arrStr = record.ContainsKey("arrival_time") ? record["arrival_time"].ToString()! : string.Empty;
            var depStr = record.ContainsKey("departure_time") ? record["departure_time"].ToString()! : string.Empty;
            var arr = string.IsNullOrWhiteSpace(arrStr) ? -1 : HmsToSec(arrStr, $"trip_id={tid},field=arrival_time");
            var dep = string.IsNullOrWhiteSpace(depStr) ? -1 : HmsToSec(depStr, $"trip_id={tid},field=departure_time");

            if (arr < 0 && dep >= 0) arr = dep;
            if (dep < 0 && arr >= 0) dep = arr;

            if (!data.StopTimes.TryGetValue(tid, out var times))
                data.StopTimes[tid] = times = [];

            times.Add(new StopTime { StopId = sid, StopSequence = seq, ArrivalSec = arr, DepartureSec = dep });
        }

        foreach (var list in data.StopTimes.Values)
            list.Sort((a, b) => a.StopSequence.CompareTo(b.StopSequence));
    }

    private void LoadCalendar(ZipArchive zip, GtfsData data)
    {
        if (zip.GetEntry("calendar.txt") == null)
        {
            _logger.LogWarning("calendar.txt not found — all trips treated as active");
            return;
        }

        string[] dowCols = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];
        using var reader = OpenEntry(zip, "calendar.txt");
        using var csv = new CsvReader(reader, CsvConfig());

        foreach (var row in csv.GetRecords<dynamic>())
        {
            var record = (IDictionary<string, object>)row;
            var svc = record["service_id"].ToString()!.Trim();
            var start = ParseDate(record["start_date"].ToString()!, "calendar.txt:start_date");
            var end = ParseDate(record["end_date"].ToString()!, "calendar.txt:end_date");
            var days = dowCols.Select(d => record[d].ToString()!.Trim() == "1").ToArray();
            var dates = new HashSet<DateOnly>();

            for (var cur = start; cur <= end; cur = cur.AddDays(1))
            {
                if (days[(int)cur.DayOfWeek == 0 ? 6 : (int)cur.DayOfWeek - 1])
                    dates.Add(cur);
            }

            data.Calendar[svc] = dates;
        }
    }

    private static StreamReader OpenEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new FileNotFoundException($"{name} not in GTFS zip");
        return new StreamReader(entry.Open(), System.Text.Encoding.UTF8);
    }

    private static CsvConfiguration CsvConfig() =>
        new(CultureInfo.InvariantCulture) { HasHeaderRecord = true, MissingFieldFound = null, BadDataFound = null };

    private int HmsToSec(string hms, string context)
    {
        if (string.IsNullOrWhiteSpace(hms)) return 0;
        var parts = hms.Trim().Split(':');
        if (parts.Length < 2)
        {
            _logger.LogDebug("Invalid GTFS time '{Value}' for {Context}: expected HH:mm[:ss]", hms, context);
            return -1;
        }

        if (!int.TryParse(parts[0], out var hours) || !int.TryParse(parts[1], out var minutes))
        {
            _logger.LogDebug("Invalid GTFS time '{Value}' for {Context}: non-numeric hour/minute", hms, context);
            return -1;
        }

        var seconds = 0;
        if (parts.Length > 2 && !int.TryParse(parts[2], out seconds))
        {
            _logger.LogDebug("Invalid GTFS time '{Value}' for {Context}: non-numeric seconds", hms, context);
            return -1;
        }

        return hours * 3600 + minutes * 60 + seconds;
    }

    private static DateOnly ParseDate(string s, string context) =>
        s.Length >= 8
        && int.TryParse(s[..4], out var year)
        && int.TryParse(s[4..6], out var month)
        && int.TryParse(s[6..8], out var day)
        && DateOnly.TryParse($"{year:D4}-{month:D2}-{day:D2}", out var parsed)
            ? parsed
            : throw new FormatException($"Invalid GTFS date value '{s}' in {context}.");
}
