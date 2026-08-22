using System.IO.Compression;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Routing.Export;

/// <summary>
/// Exports the whole reconciled network as a single GTFS dataset for the OTP graph build.
/// <para>
/// This is deliberately <b>one bundle for the whole map</b>, not one per feed, and deliberately
/// <b>operator-agnostic</b>: selection is a spatial + status query over the canonical model — every
/// active <c>FeedVersion</c>, every in-scope active <c>RawStop</c> and <c>CanonicalStation</c>, every
/// active <c>CanonicalRoute</c> — with no operator id anywhere. Adding an operator is inserting rows;
/// the next export picks it up with no code change.
/// </para>
/// <para>
/// Two schema realities shape the output. Raw stop ids are unique only within a feed version, so
/// exported stop/trip/service ids are namespaced by version (<see cref="ExportedStopId"/>). And
/// reconciliation lives in <c>CanonicalStation</c>/<c>CanonicalRoute</c>, so those are emitted as the
/// cross-operator anchors: canonical stations as <c>location_type=1</c> parents keyed by
/// <c>OnestopId</c>, canonical routes as <c>routes.txt</c> keyed by <c>OnestopId</c>. That is what
/// carries reconciliation into routing — OTP treats a parent station as one place for transfers — for
/// free, without rewriting a single trip.
/// </para>
/// </summary>
public sealed class GtfsBundleExporter(
    TransitDbContext db,
    IOptions<RoutingOptions> options,
    ILogger<GtfsBundleExporter> logger)
{
    public async Task<GtfsExportResult> ExportAsync(CancellationToken ct = default)
    {
        var scope = options.Value.Scope;
        var timezone = string.IsNullOrWhiteSpace(options.Value.Timezone) ? "UTC" : options.Value.Timezone;

        // Active feed versions, gated on both the version and its feed being active — so deactivating
        // a feed drops exactly its trips from the next bundle.
        var activeVersionIds = await (
            from fv in db.FeedVersions.AsNoTracking()
            where fv.IsActive
            join f in db.Feeds on fv.FeedId equals f.Id
            where f.IsActive
            select fv.Id).ToListAsync(ct);
        var activeSet = activeVersionIds.ToHashSet();

        // --- Stops: raw stops (children) + canonical stations (parents), spatially scoped. ---
        var rawStops = (await db.RawStops.AsNoTracking()
                .Where(rs => rs.IsActive && activeSet.Contains(rs.FeedVersionId))
                .Select(rs => new RawStopRow(rs.Id, rs.FeedVersionId, rs.RawStopId, rs.Name, rs.Lat, rs.Lon, rs.CanonicalStationId))
                .ToListAsync(ct))
            .Where(rs => GeoBounds.IsUsable(rs.Lat, rs.Lon) && scope.Contains(rs.Lat, rs.Lon))
            .ToList();

        var canonicalStations = (await db.CanonicalStations.AsNoTracking()
                .Where(cs => cs.IsActive)
                .Select(cs => new CanonicalStationRow(cs.Id, cs.OnestopId, cs.Name, cs.Latitude, cs.Longitude))
                .ToListAsync(ct))
            .Where(cs => GeoBounds.IsUsable(cs.Lat, cs.Lon) && scope.Contains(cs.Lat, cs.Lon))
            .ToList();

        var rawStopsById = rawStops.ToDictionary(rs => rs.Id, rs => new RawStopRef(rs.FeedVersionId, rs.RawStopId));
        var canonicalOnestopById = canonicalStations.ToDictionary(cs => cs.Id, cs => cs.OnestopId);
        var resolver = new StopTimeResolver(rawStopsById, canonicalOnestopById);

        // --- Routes come from CanonicalRoute; their operator becomes the GTFS agency. ---
        var routes = await db.CanonicalRoutes.AsNoTracking()
            .Where(cr => cr.IsActive)
            .Select(cr => new RouteRow(cr.Id, cr.OnestopId, cr.ShortName, cr.LongName, cr.RouteType, cr.Color, cr.TextColor, cr.OperatorId))
            .ToListAsync(ct);
        var routeById = routes.ToDictionary(r => r.Id);

        var operatorIds = routes.Select(r => r.OperatorId).Distinct().ToList();
        var operators = (await db.Operators.AsNoTracking()
                .Where(o => operatorIds.Contains(o.Id))
                .Select(o => new OperatorRow(o.Id, o.GlobalId, o.OnestopId, o.Name, o.Website))
                .ToListAsync(ct))
            .ToDictionary(o => o.Id);

        // --- Trips: only those attached to an exported canonical route are routable. ---
        var trips = await db.Trips.AsNoTracking()
            .Where(t => activeSet.Contains(t.FeedVersionId))
            .Select(t => new { t.Id, t.FeedVersionId, t.TripId, t.ServiceId, t.CanonicalRouteId, t.TripHeadsign, t.DirectionId })
            .ToListAsync(ct);

        var tripInfo = new Dictionary<int, TripExport>(trips.Count);
        var tripsSkippedNoRoute = 0;
        foreach (var t in trips)
        {
            if (t.CanonicalRouteId is not int routeId || !routeById.TryGetValue(routeId, out var route))
            {
                tripsSkippedNoRoute++;
                continue; // a trip with no active canonical route has nowhere to hang in routes.txt
            }

            tripInfo[t.Id] = new TripExport(
                FeedVersionId: t.FeedVersionId,
                ExportedTripId: ExportedStopId.Encode(t.FeedVersionId, t.TripId),
                RouteOnestopId: route.OnestopId,
                ExportedServiceId: ExportedStopId.Encode(t.FeedVersionId, t.ServiceId),
                Headsign: t.TripHeadsign,
                DirectionId: t.DirectionId);
        }

        var report = new ResolutionReport();

        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteAgencies(zip, operators, timezone);
            WriteStops(zip, rawStops, canonicalStations, canonicalOnestopById);
            WriteRoutes(zip, routes, operators);
            await WriteCalendarsAsync(zip, activeSet, ct);
            await WriteCalendarDatesAsync(zip, activeSet, ct);
            WriteTrips(zip, tripInfo.Values);
            await WriteStopTimesAsync(zip, activeSet, tripInfo, resolver, report, ct);
            WriteFeedInfo(zip);
        }

        if (report.AnyDropped)
        {
            logger.LogWarning(
                "GTFS export dropped {Dropped} stop time(s) that resolved to no stop; {Versions} version(s) affected. Trips skipped for having no active canonical route: {Skipped}.",
                report.TotalDropped, report.ByFeedVersion.Count(v => v.Value.Dropped > 0), tripsSkippedNoRoute);
        }

        memory.Position = 0;
        return new GtfsExportResult(memory.ToArray(), report, tripsSkippedNoRoute, rawStops.Count, canonicalStations.Count, routes.Count);
    }

    /// <summary>The agency id an operator is emitted under — its cross-system GlobalId where present.</summary>
    private static string AgencyId(OperatorRow op)
    {
        if (!string.IsNullOrWhiteSpace(op.GlobalId)) return op.GlobalId;
        if (!string.IsNullOrWhiteSpace(op.OnestopId)) return op.OnestopId;
        return $"op-{op.Id}";
    }

    private static void WriteAgencies(ZipArchive zip, IReadOnlyDictionary<int, OperatorRow> operators, string timezone)
    {
        using var w = Open(zip, "agency.txt");
        w.WriteHeader("agency_id", "agency_name", "agency_url", "agency_timezone");
        foreach (var op in operators.Values)
        {
            var name = string.IsNullOrWhiteSpace(op.Name) ? AgencyId(op) : op.Name;
            var url = string.IsNullOrWhiteSpace(op.Website) ? "https://example.invalid" : op.Website!;
            w.WriteRow(AgencyId(op), name, url, timezone);
        }
        w.Flush();
    }

    private static void WriteStops(
        ZipArchive zip,
        IReadOnlyList<RawStopRow> rawStops,
        IReadOnlyList<CanonicalStationRow> canonicalStations,
        Dictionary<int, string> canonicalOnestopById)
    {
        using var w = Open(zip, "stops.txt");
        w.WriteHeader("stop_id", "stop_name", "stop_lat", "stop_lon", "location_type", "parent_station");

        // Parents first (not required by GTFS, but keeps the file readable).
        foreach (var cs in canonicalStations)
        {
            w.WriteRow(cs.OnestopId, cs.Name,
                GtfsCsvWriter.Coord(cs.Lat), GtfsCsvWriter.Coord(cs.Lon), "1", null);
        }

        foreach (var rs in rawStops)
        {
            string? parent = null;
            if (rs.CanonicalStationId is int cid && canonicalOnestopById.TryGetValue(cid, out var onestop))
                parent = onestop;

            w.WriteRow(
                ExportedStopId.Encode(rs.FeedVersionId, rs.RawStopId),
                rs.Name,
                GtfsCsvWriter.Coord(rs.Lat), GtfsCsvWriter.Coord(rs.Lon), "0", parent);
        }
        w.Flush();
    }

    private static void WriteRoutes(ZipArchive zip, IReadOnlyList<RouteRow> routes, IReadOnlyDictionary<int, OperatorRow> operators)
    {
        using var w = Open(zip, "routes.txt");
        w.WriteHeader("route_id", "agency_id", "route_short_name", "route_long_name", "route_type", "route_color", "route_text_color");
        foreach (var r in routes)
        {
            var agencyId = operators.TryGetValue(r.OperatorId, out var op) ? AgencyId(op) : $"op-{r.OperatorId}";
            w.WriteRow(r.OnestopId, agencyId, r.ShortName, r.LongName, GtfsCsvWriter.Number((int)r.RouteType), r.Color, r.TextColor);
        }
        w.Flush();
    }

    private async Task WriteCalendarsAsync(ZipArchive zip, HashSet<int> activeSet, CancellationToken ct)
    {
        using var w = Open(zip, "calendar.txt");
        w.WriteHeader("service_id", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday", "start_date", "end_date");
        var rows = db.Calendars.AsNoTracking()
            .Where(c => activeSet.Contains(c.FeedVersionId))
            .Select(c => new { c.FeedVersionId, c.ServiceId, c.Monday, c.Tuesday, c.Wednesday, c.Thursday, c.Friday, c.Saturday, c.Sunday, c.StartDate, c.EndDate })
            .AsAsyncEnumerable();
        await foreach (var c in rows.WithCancellation(ct))
        {
            w.WriteRow(
                ExportedStopId.Encode(c.FeedVersionId, c.ServiceId),
                B(c.Monday), B(c.Tuesday), B(c.Wednesday), B(c.Thursday), B(c.Friday), B(c.Saturday), B(c.Sunday),
                GtfsCsvWriter.Date(c.StartDate), GtfsCsvWriter.Date(c.EndDate));
        }
        w.Flush();

        static string B(bool value) => value ? "1" : "0";
    }

    private async Task WriteCalendarDatesAsync(ZipArchive zip, HashSet<int> activeSet, CancellationToken ct)
    {
        using var w = Open(zip, "calendar_dates.txt");
        w.WriteHeader("service_id", "date", "exception_type");
        var rows = db.CalendarDates.AsNoTracking()
            .Where(cd => activeSet.Contains(cd.FeedVersionId))
            .Select(cd => new { cd.FeedVersionId, cd.ServiceId, cd.Date, cd.ExceptionType })
            .AsAsyncEnumerable();
        await foreach (var cd in rows.WithCancellation(ct))
        {
            w.WriteRow(
                ExportedStopId.Encode(cd.FeedVersionId, cd.ServiceId),
                GtfsCsvWriter.Date(cd.Date),
                GtfsCsvWriter.Number(cd.ExceptionType));
        }
        w.Flush();
    }

    private static void WriteTrips(ZipArchive zip, IEnumerable<TripExport> trips)
    {
        using var w = Open(zip, "trips.txt");
        w.WriteHeader("route_id", "service_id", "trip_id", "trip_headsign", "direction_id");
        foreach (var t in trips)
        {
            w.WriteRow(t.RouteOnestopId, t.ExportedServiceId, t.ExportedTripId, t.Headsign,
                t.DirectionId is int d ? GtfsCsvWriter.Number(d) : null);
        }
        w.Flush();
    }

    private async Task WriteStopTimesAsync(
        ZipArchive zip, HashSet<int> activeSet, IReadOnlyDictionary<int, TripExport> tripInfo,
        StopTimeResolver resolver, ResolutionReport report, CancellationToken ct)
    {
        using var w = Open(zip, "stop_times.txt");
        w.WriteHeader("trip_id", "arrival_time", "departure_time", "stop_id", "stop_sequence");

        // Streamed and joined through Trip so we never materialize the (potentially millions of) rows,
        // and ordered so each trip's stop times are contiguous and sequential.
        var rows = (
            from st in db.StopTimes.AsNoTracking()
            join t in db.Trips on st.TripId equals t.Id
            where activeSet.Contains(t.FeedVersionId)
            orderby st.TripId, st.StopSequence
            select new { st.TripId, st.RawStopEntityId, st.CanonicalStationId, st.RawStopId, st.ArrivalTime, st.DepartureTime, st.StopSequence })
            .AsAsyncEnumerable();

        await foreach (var st in rows.WithCancellation(ct))
        {
            if (!tripInfo.TryGetValue(st.TripId, out var trip))
                continue; // trip skipped for having no canonical route; already counted

            var resolved = resolver.Resolve(st.RawStopEntityId, st.CanonicalStationId, trip.FeedVersionId, st.RawStopId);
            report.Record(trip.FeedVersionId, resolved);
            if (resolved.Kind == ExportedStopKind.Dropped)
                continue;

            w.WriteRow(
                trip.ExportedTripId,
                GtfsCsvWriter.Time(st.ArrivalTime),
                GtfsCsvWriter.Time(st.DepartureTime),
                resolved.StopId,
                GtfsCsvWriter.Number(st.StopSequence));
        }
        w.Flush();
    }

    private static void WriteFeedInfo(ZipArchive zip)
    {
        using var w = Open(zip, "feed_info.txt");
        w.WriteHeader("feed_publisher_name", "feed_publisher_url", "feed_lang", "feed_version");
        w.WriteRow("TransitInfoAPI", "https://example.invalid", "hr",
            DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture));
        w.Flush();
    }

    private static GtfsCsvWriter Open(ZipArchive zip, string name)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        return new GtfsCsvWriter(entry.Open());
    }

    private readonly record struct TripExport(
        int FeedVersionId, string ExportedTripId, string RouteOnestopId,
        string ExportedServiceId, string? Headsign, int? DirectionId);

    private readonly record struct RawStopRow(int Id, int FeedVersionId, string RawStopId, string Name, double Lat, double Lon, int? CanonicalStationId);
    private readonly record struct CanonicalStationRow(int Id, string OnestopId, string Name, double Lat, double Lon);
    private readonly record struct RouteRow(int Id, string OnestopId, string ShortName, string LongName, RouteType RouteType, string? Color, string? TextColor, int OperatorId);
    private readonly record struct OperatorRow(int Id, string GlobalId, string OnestopId, string Name, string? Website);
}

/// <summary>The exported bundle plus the visibility the plan requires: what resolved and what didn't.</summary>
public sealed record GtfsExportResult(
    byte[] GtfsZip,
    ResolutionReport Resolution,
    int TripsSkippedNoRoute,
    int RawStopCount,
    int CanonicalStationCount,
    int RouteCount);
