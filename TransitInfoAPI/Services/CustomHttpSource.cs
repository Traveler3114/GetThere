using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
using TransitInfoAPI.Core;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Services;

/// <summary>
/// Builds a <see cref="TransitDocument"/> from an operator's HTTP API.
/// <para>
/// Each configured request fills one section, so a source that only exposes stops produces a
/// stops-only document rather than failing for the sections it was never going to have. Whatever the
/// operator does not publish and can be derived without guessing is filled by
/// <see cref="TransitDocumentCompleter"/>.
/// </para>
/// </summary>
public class CustomHttpSource : ITransitSource
{
    private readonly TransitDbContext _db;
    private readonly CustomSourceEngine _engine;
    private readonly TransitDocumentCompleter _completer;
    private readonly ImportLogStore _logStore;
    private readonly IWebHostEnvironment _env;
    private readonly CustomExtractorRegistry _extractors;
    private readonly IHttpClientFactory _httpFactory;
    private readonly SecretProtector _secrets;
    private readonly ILogger<CustomHttpSource> _logger;

    public CustomHttpSource(
        TransitDbContext db,
        CustomSourceEngine engine,
        TransitDocumentCompleter completer,
        ImportLogStore logStore,
        IWebHostEnvironment env,
        CustomExtractorRegistry extractors,
        IHttpClientFactory httpFactory,
        SecretProtector secrets,
        ILogger<CustomHttpSource> logger)
    {
        _db = db;
        _engine = engine;
        _completer = completer;
        _logStore = logStore;
        _env = env;
        _extractors = extractors;
        _httpFactory = httpFactory;
        _secrets = secrets;
        _logger = logger;
    }

    /// <summary>
    /// Writes an extractor-produced document back into the snapshot's flat sections, so the
    /// on-disk artifact has one shape regardless of which path produced it.
    /// </summary>
    private static void FlattenInto(Snapshot snapshot, TransitDocument document)
    {
        void Add<T>(TransitSection section, List<T>? items, Func<T, Dictionary<string, object?>> toRow)
        {
            if (items is null) return;
            snapshot.Sections[section.ToString()] = [.. items.Select(toRow)];
        }

        Add(TransitSection.Stops, document.Stops, s => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["StopId"] = s.StopId,
            ["StopName"] = s.StopName,
            ["StopLat"] = s.StopLat,
            ["StopLon"] = s.StopLon,
            ["StopCode"] = s.StopCode,
            ["StopDesc"] = s.StopDesc,
            ["ZoneId"] = s.ZoneId,
            ["PlatformCode"] = s.PlatformCode,
            ["LocationType"] = s.LocationType,
            ["ParentStation"] = s.ParentStation
        });

        Add(TransitSection.Routes, document.Routes, r => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["RouteId"] = r.RouteId,
            ["RouteShortName"] = r.RouteShortName,
            ["RouteLongName"] = r.RouteLongName,
            ["RouteType"] = r.RouteType,
            ["RouteColor"] = r.RouteColor,
            ["RouteTextColor"] = r.RouteTextColor
        });

        Add(TransitSection.Trips, document.Trips, t => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TripId"] = t.TripId,
            ["RouteId"] = t.RouteId,
            ["ServiceId"] = t.ServiceId,
            ["TripHeadsign"] = t.TripHeadsign,
            ["DirectionId"] = t.DirectionId,
            ["ShapeId"] = t.ShapeId
        });

        Add(TransitSection.Calendar, document.Calendar, c => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ServiceId"] = c.ServiceId,
            ["Monday"] = c.Monday,
            ["Tuesday"] = c.Tuesday,
            ["Wednesday"] = c.Wednesday,
            ["Thursday"] = c.Thursday,
            ["Friday"] = c.Friday,
            ["Saturday"] = c.Saturday,
            ["Sunday"] = c.Sunday,
            ["StartDate"] = c.StartDate,
            ["EndDate"] = c.EndDate
        });

        Add(TransitSection.Agencies, document.Agencies, a => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AgencyId"] = a.AgencyId,
            ["AgencyName"] = a.AgencyName,
            ["AgencyUrl"] = a.AgencyUrl,
            ["AgencyTimezone"] = a.AgencyTimezone
        });
    }

    public string Kind => "custom-http";

    public bool CanHandle(Feed feed) => feed.CustomSourceId is not null;

    /// <summary>
    /// Extracts everything, writes it to the feed's storage directory as a single JSON snapshot, and
    /// hashes that. The snapshot is what makes a custom feed re-importable without re-fetching, and
    /// what gives it a stable content hash — the same role <c>gtfs.zip</c> plays for an archive.
    /// </summary>
    public async Task<SourceFetchResult?> FetchAsync(Feed feed, CancellationToken ct)
    {
        var source = await LoadSourceAsync(feed, ct);
        if (source is null)
        {
            _logger.LogWarning("Feed {FeedId} points at custom source {SourceId}, which does not exist", feed.FeedId, feed.CustomSourceId);
            return null;
        }

        var snapshot = await ExtractSnapshotAsync(source, maxRowsPerRequest: null, ct);

        var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, SnapshotOptions);

        var storage = new FeedStorage(_env.ContentRootPath);
        var dir = storage.DirectoryFor(feed.FeedId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, SnapshotFileName);
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, json, ct);
        File.Move(tmp, path, overwrite: true);

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(json)).ToLowerInvariant();
        return new SourceFetchResult(hash, json.Length, "application/json");
    }

    public async Task<SourceReadResult> OpenAsync(Feed feed, int feedVersionId, CancellationToken ct)
    {
        var source = await LoadSourceAsync(feed, ct);
        if (source is null)
            return SourceReadResult.Fail($"Custom source {feed.CustomSourceId} not found.");

        var path = Path.Combine(new FeedStorage(_env.ContentRootPath).DirectoryFor(feed.FeedId), SnapshotFileName);
        if (!File.Exists(path))
            return SourceReadResult.Fail("Custom source snapshot not found on disk.");

        Snapshot? snapshot;
        try
        {
            await using var stream = File.OpenRead(path);
            snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(stream, SnapshotOptions, ct);
        }
        catch (JsonException ex)
        {
            return SourceReadResult.Fail($"Custom source snapshot is unreadable: {ex.Message}");
        }

        if (snapshot is null)
            return SourceReadResult.Fail("Custom source snapshot is empty.");

        foreach (var line in snapshot.Log)
            _logStore.AddEntry(feedVersionId, line);

        var document = BuildDocument(snapshot, feed.OperatorId, out var buildWarnings);
        foreach (var warning in buildWarnings)
            _logStore.AddEntry(feedVersionId, $"Warning: {warning}");

        var stopTimes = snapshot.Sections.TryGetValue(nameof(TransitSection.StopTimes), out var stRows) && stRows.Count > 0
            ? Batched(ToStopTimes(stRows))
            : null;

        var completed = await _completer.CompleteAsync(
            document,
            source.Operator,
            source is { ServiceWindowStart: { } s, ServiceWindowEnd: { } e } ? (s, e) : null,
            stopTimes,
            ct);

        if (completed.Stops is null or { Count: 0 })
            return SourceReadResult.Fail("Custom source produced no stops — there is nothing to import.");

        _logStore.AddEntry(feedVersionId, $"Extracted: {completed.Summary()}");
        _logStore.AddEntry(feedVersionId, $"Completeness: {completed.Completeness}");
        if (completed.SynthesizedSections.Count > 0)
            _logStore.AddEntry(feedVersionId, $"Synthesized: {string.Join(", ", completed.SynthesizedSections)}");

        return new SourceReadResult { Document = completed, StopTimes = stopTimes };
    }

    // ── Extraction ────────────────────────────────────────────────────────────────────────────

    internal const string SnapshotFileName = "custom-source.json";

    private static readonly JsonSerializerOptions SnapshotOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The mapped, un-typed result of every request. Persisted rather than the raw responses so a
    /// re-import replays exactly what the mapping produced at fetch time.
    /// </summary>
    internal sealed class Snapshot
    {
        public Dictionary<string, List<Dictionary<string, object?>>> Sections { get; set; } = [];
        public List<string> Log { get; set; } = [];
        public List<string> Warnings { get; set; } = [];
    }

    internal async Task<Snapshot> ExtractSnapshotAsync(CustomSource source, int? maxRowsPerRequest, CancellationToken ct)
    {
        var snapshot = new Snapshot();

        // A named extractor takes the whole job. Its document is flattened back into the same
        // snapshot shape so everything downstream — versioning, replay from disk, the preview —
        // stays identical whether the rows came from configuration or from code.
        var extractor = _extractors.For(source.ExtractorKey);
        if (extractor is not null)
        {
            snapshot.Log.Add($"Extractor '{extractor.Key}' handling this source");
            var produced = await extractor.ExtractAsync(source, _httpFactory.CreateClient("gtfs"), ct);
            FlattenInto(snapshot, produced);
            return snapshot;
        }

        if (!string.IsNullOrWhiteSpace(source.ExtractorKey))
        {
            snapshot.Warnings.Add(
                $"No extractor registered under '{source.ExtractorKey}' — falling back to the configured requests");
        }

        foreach (var request in source.Requests.OrderBy(r => r.SortOrder))
        {
            var extraction = await _engine.ExecuteAsync(request, _secrets.Unprotect(source.AuthConfig), maxRowsPerRequest, ct);
            snapshot.Log.AddRange(extraction.Log);
            snapshot.Warnings.AddRange(extraction.Warnings);

            var mapped = CustomSourceEngine.ApplyMappings(extraction.Rows, [.. request.Mappings]);

            // Deduplicated after mapping so DistinctBy names a target field (StopId, RouteId) rather
            // than whatever the operator called it in their own payload.
            mapped = CustomSourceEngine.Deduplicate(mapped, request.DistinctBy, out var dropped);

            var key = request.TargetSection.ToString();

            if (!snapshot.Sections.TryGetValue(key, out var existing))
                snapshot.Sections[key] = existing = [];

            existing.AddRange(mapped.Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase)));

            if (dropped > 0)
                snapshot.Log.Add($"{key}: dropped {dropped:N0} duplicate row(s) on {request.DistinctBy}");
            snapshot.Log.Add($"{key}: {mapped.Count:N0} row(s) mapped");
        }

        return snapshot;
    }

    // ── Row → record ──────────────────────────────────────────────────────────────────────────

    internal static TransitDocument BuildDocument(Snapshot snapshot, int operatorId, out List<string> warnings)
    {
        warnings = [];

        var stops = Section(snapshot, TransitSection.Stops, ToStops, warnings);
        var routes = Section(snapshot, TransitSection.Routes, ToRoutes, warnings);
        var trips = Section(snapshot, TransitSection.Trips, ToTrips, warnings);
        var agencies = Section(snapshot, TransitSection.Agencies, ToAgencies, warnings);
        var calendar = Section(snapshot, TransitSection.Calendar, ToCalendars, warnings);
        var calendarDates = Section(snapshot, TransitSection.CalendarDates, ToCalendarDates, warnings);

        var hasStopTimes = snapshot.Sections.TryGetValue(nameof(TransitSection.StopTimes), out var st) && st.Count > 0;

        return new TransitDocument
        {
            OperatorId = operatorId,
            Agencies = agencies,
            Stops = stops,
            Routes = routes,
            Trips = trips,
            Calendar = calendar,
            CalendarDates = calendarDates,
            HasStopTimes = hasStopTimes
        };
    }

    /// <summary>Absent stays null — "not published" and "published empty" are different facts.</summary>
    private static List<T>? Section<T>(
        Snapshot snapshot,
        TransitSection section,
        Func<List<Dictionary<string, object?>>, List<string>, List<T>> convert,
        List<string> warnings)
    {
        return snapshot.Sections.TryGetValue(section.ToString(), out var rows)
            ? convert(rows, warnings)
            : null;
    }

    private static List<RawStopRecord> ToStops(List<Dictionary<string, object?>> rows, List<string> warnings)
    {
        var stops = new List<RawStopRecord>(rows.Count);
        var dropped = 0;

        foreach (var row in rows)
        {
            var lat = Num(row, "StopLat");
            var lon = Num(row, "StopLon");

            // The shared predicate the GTFS parser also applies: a (0,0) stop puts a pin in the
            // Atlantic and drags the feed's convex hull with it.
            if (lat is null || lon is null || !GeoBounds.IsUsable(lat.Value, lon.Value))
            {
                dropped++;
                continue;
            }

            var id = Str(row, "StopId");
            if (string.IsNullOrWhiteSpace(id))
            {
                dropped++;
                continue;
            }

            stops.Add(new RawStopRecord
            {
                StopId = id,
                StopName = Str(row, "StopName") ?? id,
                StopLat = lat.Value,
                StopLon = lon.Value,
                StopCode = Str(row, "StopCode"),
                StopDesc = Str(row, "StopDesc"),
                ZoneId = Str(row, "ZoneId"),
                PlatformCode = Str(row, "PlatformCode"),
                LocationType = (int?)Num(row, "LocationType"),
                ParentStation = Str(row, "ParentStation")
            });
        }

        if (dropped > 0)
            warnings.Add($"Dropped {dropped} stop(s) with a missing id or invalid coordinates");

        return stops;
    }

    private static List<RawRouteRecord> ToRoutes(List<Dictionary<string, object?>> rows, List<string> warnings)
    {
        var routes = new List<RawRouteRecord>(rows.Count);
        var dropped = 0;

        foreach (var row in rows)
        {
            var id = Str(row, "RouteId");
            if (string.IsNullOrWhiteSpace(id)) { dropped++; continue; }

            routes.Add(new RawRouteRecord
            {
                RouteId = id,
                RouteShortName = Str(row, "RouteShortName") ?? string.Empty,
                RouteLongName = Str(row, "RouteLongName") ?? string.Empty,
                RouteType = (int?)Num(row, "RouteType") ?? 3,
                RouteColor = Str(row, "RouteColor"),
                RouteTextColor = Str(row, "RouteTextColor"),
                AgencyId = Str(row, "AgencyId")
            });
        }

        if (dropped > 0)
            warnings.Add($"Dropped {dropped} route(s) with no route id");

        return routes;
    }

    private static List<RawTripRecord> ToTrips(List<Dictionary<string, object?>> rows, List<string> warnings)
    {
        var trips = new List<RawTripRecord>(rows.Count);
        var dropped = 0;

        foreach (var row in rows)
        {
            var id = Str(row, "TripId");
            if (string.IsNullOrWhiteSpace(id)) { dropped++; continue; }

            trips.Add(new RawTripRecord
            {
                TripId = id,
                RouteId = Str(row, "RouteId") ?? string.Empty,
                ServiceId = Str(row, "ServiceId") ?? string.Empty,
                TripHeadsign = Str(row, "TripHeadsign"),
                TripShortName = Str(row, "TripShortName"),
                DirectionId = (int?)Num(row, "DirectionId"),
                ShapeId = Str(row, "ShapeId")
            });
        }

        if (dropped > 0)
            warnings.Add($"Dropped {dropped} trip(s) with no trip id");

        return trips;
    }

    private static List<RawAgencyRecord> ToAgencies(List<Dictionary<string, object?>> rows, List<string> warnings)
        => [.. rows.Select(row => new RawAgencyRecord
        {
            AgencyId = Str(row, "AgencyId") ?? string.Empty,
            AgencyName = Str(row, "AgencyName") ?? string.Empty,
            AgencyUrl = Str(row, "AgencyUrl"),
            AgencyTimezone = Str(row, "AgencyTimezone"),
            AgencyLang = Str(row, "AgencyLang"),
            AgencyPhone = Str(row, "AgencyPhone")
        })];

    private static List<RawCalendarRecord> ToCalendars(List<Dictionary<string, object?>> rows, List<string> warnings)
        => [.. rows.Select(row => new RawCalendarRecord
        {
            ServiceId = Str(row, "ServiceId") ?? string.Empty,
            Monday = (int?)Num(row, "Monday") ?? 0,
            Tuesday = (int?)Num(row, "Tuesday") ?? 0,
            Wednesday = (int?)Num(row, "Wednesday") ?? 0,
            Thursday = (int?)Num(row, "Thursday") ?? 0,
            Friday = (int?)Num(row, "Friday") ?? 0,
            Saturday = (int?)Num(row, "Saturday") ?? 0,
            Sunday = (int?)Num(row, "Sunday") ?? 0,
            StartDate = Str(row, "StartDate") ?? string.Empty,
            EndDate = Str(row, "EndDate") ?? string.Empty
        })];

    private static List<RawCalendarDateRecord> ToCalendarDates(List<Dictionary<string, object?>> rows, List<string> warnings)
        => [.. rows.Select(row => new RawCalendarDateRecord
        {
            ServiceId = Str(row, "ServiceId") ?? string.Empty,
            Date = Str(row, "Date") ?? string.Empty,
            ExceptionType = (int?)Num(row, "ExceptionType") ?? 1
        })];

    private static List<RawStopTimeRecord> ToStopTimes(List<Dictionary<string, object?>> rows)
    {
        var stopTimes = new List<RawStopTimeRecord>(rows.Count);

        foreach (var row in rows)
        {
            var tripId = Str(row, "TripId");
            var stopId = Str(row, "StopId");
            if (string.IsNullOrWhiteSpace(tripId) || string.IsNullOrWhiteSpace(stopId))
                continue;

            var departure = Str(row, "DepartureTime");
            var arrival = Str(row, "ArrivalTime");

            stopTimes.Add(new RawStopTimeRecord
            {
                TripId = tripId,
                StopId = stopId,
                ArrivalTime = arrival ?? departure ?? string.Empty,
                DepartureTime = departure ?? arrival ?? string.Empty,
                StopSequence = (int?)Num(row, "StopSequence") ?? 0,
                StopHeadsign = Str(row, "StopHeadsign"),
                PickupType = (int?)Num(row, "PickupType"),
                DropOffType = (int?)Num(row, "DropOffType")
            });
        }

        return stopTimes;
    }

    /// <summary>
    /// Re-batches an in-memory list to match the streaming contract the import phases expect. A
    /// custom source's rows are already materialised — the batching exists so
    /// <c>ImportStopTimesBulkPhaseAsync</c> sees one shape regardless of where the rows came from.
    /// </summary>
    private static Func<IAsyncEnumerable<List<RawStopTimeRecord>>> Batched(List<RawStopTimeRecord> all, int batchSize = 50000)
        => () => Enumerate(all, batchSize);

    private static async IAsyncEnumerable<List<RawStopTimeRecord>> Enumerate(List<RawStopTimeRecord> all, int batchSize)
    {
        for (var i = 0; i < all.Count; i += batchSize)
        {
            yield return all.GetRange(i, Math.Min(batchSize, all.Count - i));
            await Task.Yield();
        }
    }

    private static string? Str(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null) return null;
        if (value is JsonElement je)
            return je.ValueKind == JsonValueKind.String ? je.GetString() : je.GetRawText().Trim('"');
        var s = value.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>
    /// Reads a numeric field, treating anything that is not a finite number as absent.
    /// <para>
    /// The finiteness check is not decoration. JSON cannot express NaN, but a source is free to send
    /// the <em>string</em> <c>"NaN"</c> — or <c>"Infinity"</c> — and <c>double.TryParse</c> accepts
    /// both against <c>InvariantCulture</c>'s symbols, so the value reached <c>ToStops</c> as a real
    /// NaN. Every comparison against NaN is false, so <c>lat is &lt; -90 or &gt; 90</c> did not
    /// reject it and <c>lat == 0 &amp;&amp; lon == 0</c> did not either: the coordinate guard, which
    /// exists precisely to keep junk out of the feed's geometry, passed it straight through to a
    /// SQL Server <c>float</c> column that has no NaN to store it in. The import then failed on a
    /// bulk-copy error naming neither the stop nor the reason, instead of dropping one row with a
    /// warning.
    /// </para>
    /// <para>
    /// Returning null here fixes it for every caller rather than only for coordinates —
    /// <c>(int?)double.NaN</c> is 0 under .NET's saturating conversions, so a NaN
    /// <c>LocationType</c> or <c>StopSequence</c> silently became a meaningful 0.
    /// </para>
    /// </summary>
    private static double? Num(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null) return null;

        // Snapshot round-tripping turns every scalar into a JsonElement, so this has to handle both
        // the freshly-extracted and the deserialized shape.
        double? parsed = value switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } number => number.GetDouble(),
            JsonElement { ValueKind: JsonValueKind.String } text => Parse(text.GetString()),
            JsonElement => null,
            double d => d,
            long l => l,
            int i => i,
            _ => Parse(value.ToString())
        };

        return parsed is { } result && double.IsFinite(result) ? result : null;

        static double? Parse(string? raw) =>
            double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var fromText) ? fromText : null;
    }

    private async Task<CustomSource?> LoadSourceAsync(Feed feed, CancellationToken ct) =>
        await _db.CustomSources
            .Include(cs => cs.Operator)
            .Include(cs => cs.Requests).ThenInclude(r => r.Mappings)
            .FirstOrDefaultAsync(cs => cs.Id == feed.CustomSourceId, ct);
}
