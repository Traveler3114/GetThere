using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetTopologySuite.Geometries;
using NetTopologySuite;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Managers;

public class CustomFeedImportOptions
{
    public double StopCountDropThreshold { get; set; } = 0.90;
    public double RouteCountDropThreshold { get; set; } = 0.90;
    public int MaxVersionRetention { get; set; } = 2;
}

public record ImportValidationResult
{
    public bool CanProceed { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public class CustomFeedDirectImporter
{
    private readonly TransitDbContext _db;
    private readonly OnestopIdManager _onestopId;
    private readonly ReconciliationManager _reconciliation;
    private readonly IOptions<CustomFeedImportOptions> _importOptions;
    private readonly ILogger<CustomFeedDirectImporter> _logger;

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _feedLocks = new();
    private static readonly GeometryFactory _shapeFactory = new(new PrecisionModel(), 4326);

    public CustomFeedDirectImporter(
        TransitDbContext db,
        OnestopIdManager onestopId,
        ReconciliationManager reconciliation,
        IOptions<CustomFeedImportOptions> importOptions,
        ILogger<CustomFeedDirectImporter> logger)
    {
        _db = db;
        _onestopId = onestopId;
        _reconciliation = reconciliation;
        _importOptions = importOptions;
        _logger = logger;
    }

    public List<RawStop> MapStops(List<Dictionary<string, object?>> records)
    {
        var stops = new List<RawStop>(records.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var rawStopId = GetString(record, "stop_id") ?? string.Empty;

            if (!seenIds.Add(rawStopId))
            {
                _logger.LogWarning("Skipping duplicate stop_id {StopId}", rawStopId);
                continue;
            }

            var lat = GetDouble(record, "stop_lat") ?? 0.0;
            var lon = GetDouble(record, "stop_lon") ?? 0.0;

            if (lat < -90 || lat > 90 || lon < -180 || lon > 180
                || (lat == 0.0 && lon == 0.0))
            {
                _logger.LogWarning("Skipping stop {StopId} with invalid coordinates ({Lat}, {Lon})",
                    rawStopId, lat, lon);
                continue;
            }

            stops.Add(new RawStop
            {
                RawStopId = rawStopId,
                Name = GetString(record, "stop_name") ?? string.Empty,
                Lat = lat,
                Lon = lon,
                StationType = MapStationType(GetInt(record, "location_type")),
                ParentRawStopId = GetString(record, "parent_station"),
                StopCode = GetString(record, "stop_code"),
                StopDesc = GetString(record, "stop_desc"),
                ZoneId = GetString(record, "zone_id"),
                PlatformCode = GetString(record, "platform_code"),
                WheelchairBoarding = GetWheelchairBoarding(GetInt(record, "wheelchair_boarding")),
                IsActive = true,
                ReconciliationStatus = ReconciliationStatus.Pending
            });
        }
        return stops;
    }

    public List<CanonicalRoute> MapRoutes(
        List<Dictionary<string, object?>> records,
        double centerLat,
        double centerLon)
    {
        var routes = new List<CanonicalRoute>(records.Count);
        var seenOnestopIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenRouteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var routeId = GetString(record, "route_id") ?? string.Empty;

            if (!seenRouteIds.Add(routeId))
            {
                _logger.LogWarning("Skipping duplicate route_id {RouteId}", routeId);
                continue;
            }

            var shortName = GetString(record, "route_short_name") ?? string.Empty;
            var longName = GetString(record, "route_long_name") ?? string.Empty;

            var routeName = shortName;
            if (string.IsNullOrEmpty(routeName))
                routeName = longName;
            if (string.IsNullOrEmpty(routeName))
                routeName = routeId;
            if (string.IsNullOrEmpty(routeName))
                routeName = "unknown";

            var onestopId = _onestopId.GenerateRouteOnestopId(centerLat, centerLon, routeName);

            var uniqueOnestopId = onestopId;
            var dedupSuffix = 2;
            while (!seenOnestopIds.Add(uniqueOnestopId))
                uniqueOnestopId = $"{onestopId}-{dedupSuffix++}";

            var routeType = GetInt(record, "route_type");
            routes.Add(new CanonicalRoute
            {
                OnestopId = uniqueOnestopId,
                ShortName = shortName,
                LongName = longName,
                RouteType = routeType.HasValue ? MapGtfsRouteType(routeType.Value) : RouteType.Bus,
                Color = GetString(record, "route_color"),
                TextColor = GetString(record, "route_text_color"),
                IsActive = true,
                OperatorId = 0
            });
        }
        return routes;
    }

    public List<Trip> MapTrips(List<Dictionary<string, object?>> records)
    {
        var trips = new List<Trip>(records.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var tripId = GetString(record, "trip_id") ?? string.Empty;

            if (!seenIds.Add(tripId))
            {
                _logger.LogWarning("Skipping duplicate trip_id {TripId}", tripId);
                continue;
            }

            trips.Add(new Trip
            {
                TripId = tripId,
                RouteId = GetString(record, "route_id") ?? string.Empty,
                ServiceId = GetString(record, "service_id") ?? string.Empty,
                TripHeadsign = GetString(record, "trip_headsign"),
                TripShortName = GetString(record, "trip_short_name"),
                DirectionId = GetInt(record, "direction_id"),
                ShapeId = GetString(record, "shape_id"),
                WheelchairAccessible = GetInt(record, "wheelchair_accessible") switch
                {
                    1 => true,
                    2 => false,
                    _ => null
                },
                BikesAllowed = GetBoolFlag(GetInt(record, "bikes_allowed"))
            });
        }
        return trips;
    }

    public List<StopTime> MapStopTimes(List<Dictionary<string, object?>> records)
    {
        var stopTimes = new List<StopTime>(records.Count);
        foreach (var record in records)
        {
            var arrival = ParseGtfsTimeToSeconds(GetString(record, "arrival_time"));
            var departure = ParseGtfsTimeToSeconds(GetString(record, "departure_time"));

            stopTimes.Add(new StopTime
            {
                RawStopId = GetString(record, "stop_id") ?? string.Empty,
                ArrivalTime = arrival ?? 0,
                DepartureTime = departure ?? 0,
                StopSequence = GetInt(record, "stop_sequence") ?? 0,
                StopHeadsign = GetString(record, "stop_headsign"),
                PickupType = GetInt(record, "pickup_type"),
                DropOffType = GetInt(record, "drop_off_type"),
                Timepoint = GetBoolFlag(GetInt(record, "timepoint"))
            });
        }
        return stopTimes;
    }

    public List<Calendar> MapCalendar(List<Dictionary<string, object?>> records)
    {
        var calendars = new List<Calendar>(records.Count);
        foreach (var record in records)
        {
            calendars.Add(new Calendar
            {
                ServiceId = GetString(record, "service_id") ?? string.Empty,
                Monday = GetInt(record, "monday") == 1,
                Tuesday = GetInt(record, "tuesday") == 1,
                Wednesday = GetInt(record, "wednesday") == 1,
                Thursday = GetInt(record, "thursday") == 1,
                Friday = GetInt(record, "friday") == 1,
                Saturday = GetInt(record, "saturday") == 1,
                Sunday = GetInt(record, "sunday") == 1,
                StartDate = ParseDate(GetString(record, "start_date")) ?? default,
                EndDate = ParseDate(GetString(record, "end_date")) ?? default
            });
        }
        return calendars;
    }

    public List<CalendarDate> MapCalendarDates(List<Dictionary<string, object?>> records)
    {
        var calendarDates = new List<CalendarDate>(records.Count);
        foreach (var record in records)
        {
            calendarDates.Add(new CalendarDate
            {
                ServiceId = GetString(record, "service_id") ?? string.Empty,
                Date = ParseDate(GetString(record, "date")) ?? default,
                ExceptionType = GetInt(record, "exception_type") ?? 0
            });
        }
        return calendarDates;
    }

    public List<Agency> MapAgency(List<Dictionary<string, object?>> records)
    {
        var agencies = new List<Agency>(records.Count);
        foreach (var record in records)
        {
            agencies.Add(new Agency
            {
                AgencyId = GetString(record, "agency_id") ?? string.Empty,
                Name = GetString(record, "agency_name") ?? string.Empty,
                Url = GetString(record, "agency_url"),
                Timezone = GetString(record, "agency_timezone"),
                Language = GetString(record, "agency_lang"),
                Phone = GetString(record, "agency_phone"),
                FareUrl = GetString(record, "agency_fare_url"),
                Email = GetString(record, "agency_email")
            });
        }
        return agencies;
    }

    public async Task ImportAndActivateAsync(
        CustomFeed customFeed,
        Dictionary<string, List<Dictionary<string, object?>>> mappedRecords,
        CancellationToken ct = default)
    {
        var feed = await _db.Feeds
            .FirstOrDefaultAsync(f => f.CustomFeedId == customFeed.Id, ct);
        if (feed is null)
        {
            _logger.LogError(
                "No hidden Feed found for CustomFeed {CustomFeedId} — this indicates the hidden feed " +
                "was deleted or never created. Re-save the custom source in the admin UI to recreate it.",
                customFeed.Id);
            return;
        }

        var hash = CustomFeedHash.ComputeHash(mappedRecords);

        var feedLock = _feedLocks.GetOrAdd(feed.Id, _ => new SemaphoreSlim(1, 1));
        await feedLock.WaitAsync(ct);
        try
        {
            var existingVersion = await _db.FeedVersions
                .Where(fv => fv.FeedId == feed.Id && fv.Sha1 == hash)
                .OrderByDescending(fv => fv.Id)
                .FirstOrDefaultAsync(ct);

            if (existingVersion is not null)
            {
                _logger.LogInformation(
                    "CustomFeed {CustomFeedId}: content unchanged (hash {Hash}), skipping",
                    customFeed.Id, hash);
                return;
            }

            var activeVersion = await _db.FeedVersions
                .Where(fv => fv.FeedId == feed.Id && fv.IsActive)
                .OrderByDescending(fv => fv.Id)
                .FirstOrDefaultAsync(ct);

            if (!customFeed.IsScheduleCapable)
            {
                _logger.LogInformation(
                    "CustomFeed {CustomFeedId}: marked as schedule-incapable, skipping import",
                    customFeed.Id);
                return;
            }

            _logger.LogInformation(
                "CustomFeed {CustomFeedId}: hash mismatch or no active version, importing (hash {Hash})",
                customFeed.Id, hash);

            var version = new FeedVersion
            {
                FeedId = feed.Id,
                Sha1 = hash,
                FetchedAt = DateTime.UtcNow,
                ImportStatus = FeedImportStatus.Importing,
                IsActive = false
            };
            _db.FeedVersions.Add(version);
            await _db.SaveChangesAsync(ct);
            var feedVersionId = version.Id;

            var tableRecords = mappedRecords;

            var stops = GetTableRecords(tableRecords, "stops") is { } stopRecs
                ? MapStops(stopRecs) : [];
            var calendar = GetTableRecords(tableRecords, "calendar") is { } calRecs
                ? MapCalendar(calRecs) : [];
            var calendarDates = GetTableRecords(tableRecords, "calendar_dates") is { } cdRecs
                ? MapCalendarDates(cdRecs) : [];
            var agency = GetTableRecords(tableRecords, "agency") is { } agRecs
                ? MapAgency(agRecs) : [];

            var centerLat = stops.Count > 0 ? stops.Average(static s => s.Lat) : 0;
            var centerLon = stops.Count > 0 ? stops.Average(static s => s.Lon) : 0;

            var existingRoutes = await _db.CanonicalRoutes
                .Where(cr => cr.OperatorId == feed.OperatorId)
                .ToListAsync(ct);
            var existingByOnestopId = new Dictionary<string, CanonicalRoute>(
                existingRoutes.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var er in existingRoutes)
                existingByOnestopId[er.OnestopId] = er;

            var allRoutes = GetTableRecords(tableRecords, "routes") is { } routeRecs
                ? MapRoutes(routeRecs, centerLat, centerLon) : [];

            var newRoutes = allRoutes.Where(r => !existingByOnestopId.ContainsKey(r.OnestopId)).ToList();

            var routeRecordsForLookup = GetTableRecords(tableRecords, "routes") ?? [];

            foreach (var route in newRoutes)
                route.OperatorId = feed.OperatorId;

            var trips = GetTableRecords(tableRecords, "trips") is { } tripRecs
                ? MapTrips(tripRecs) : [];

            var stopTimes = GetTableRecords(tableRecords, "stop_times") is { } stRecs
                ? MapStopTimes(stRecs) : [];

            var rawShapeRecords = GetTableRecords(tableRecords, "shapes");
            List<Shape> shapes;
            if (rawShapeRecords is { Count: > 0 })
                shapes = MapShapes(rawShapeRecords);
            else
                shapes = AutoGenerateShapes(
                    trips,
                    GetTableRecords(tableRecords, "stop_times") ?? [],
                    stops);

            var stopsWithRouteType = DeriveStopRouteTypes(
                stops,
                GetTableRecords(tableRecords, "stop_times") ?? [],
                GetTableRecords(tableRecords, "trips") ?? [],
                GetTableRecords(tableRecords, "routes") ?? []);

            var validation = ValidateImport(stopsWithRouteType, calendar, calendarDates);
            if (!validation.CanProceed)
            {
                _logger.LogError(
                    "CustomFeed {CustomFeedId}: import validation failed — {Warnings}",
                    customFeed.Id, string.Join("; ", validation.Warnings));

                if (customFeed.IsScheduleCapable && stopsWithRouteType == 0)
                {
                    _logger.LogWarning(
                        "CustomFeed {CustomFeedId}: zero stops with derivable route type — " +
                        "auto-flagging as schedule-incapable",
                        customFeed.Id);
                    customFeed.IsScheduleCapable = false;
                }

                version.ImportStatus = FeedImportStatus.Failed;
                version.ImportError = string.Join("; ", validation.Warnings);
                await _db.SaveChangesAsync(ct);
                return;
            }

            foreach (var w in validation.Warnings)
                _logger.LogWarning("CustomFeed {CustomFeedId}: {Warning}", customFeed.Id, w);

            if (ShouldAbortCutover(activeVersion, stops, allRoutes))
            {
                _logger.LogError(
                    "CustomFeed {CustomFeedId}: cutover aborted — new version has {StopCount} stops " +
                    "(previous active had {PrevStopCount}) and {RouteCount} routes (previous had {PrevRouteCount})",
                    customFeed.Id, stops.Count, activeVersion?.StopCount ?? 0,
                    allRoutes.Count, activeVersion?.RouteCount ?? 0);
                version.ImportStatus = FeedImportStatus.Failed;
                version.ImportError =
                    $"Cutover aborted: stop count {stops.Count} vs previous {activeVersion?.StopCount ?? 0}, " +
                    $"route count {allRoutes.Count} vs previous {activeVersion?.RouteCount ?? 0}";
                await _db.SaveChangesAsync(ct);
                return;
            }

            foreach (var s in stops) s.FeedVersionId = feedVersionId;
            foreach (var t in trips) t.FeedVersionId = feedVersionId;
            foreach (var c in calendar) c.FeedVersionId = feedVersionId;
            foreach (var c in calendarDates) c.FeedVersionId = feedVersionId;
            foreach (var a in agency) a.FeedVersionId = feedVersionId;
            foreach (var s in shapes) s.FeedVersionId = feedVersionId;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                _db.RawStops.AddRange(stops);
                _db.CanonicalRoutes.AddRange(newRoutes);
                _db.Trips.AddRange(trips);
                _db.Calendars.AddRange(calendar);
                _db.CalendarDates.AddRange(calendarDates);
                _db.Agencies.AddRange(agency);
                await _db.SaveChangesAsync(ct);

                var allRoutesByOnestopId = new Dictionary<string, CanonicalRoute>(StringComparer.OrdinalIgnoreCase);
                foreach (var er in existingRoutes)
                    allRoutesByOnestopId[er.OnestopId] = er;
                foreach (var nr in newRoutes)
                    allRoutesByOnestopId[nr.OnestopId] = nr;

                var routeIdToRoute = new Dictionary<string, CanonicalRoute>(StringComparer.OrdinalIgnoreCase);
                foreach (var rec in routeRecordsForLookup)
                {
                    var routeId = GetString(rec, "route_id");
                    if (routeId is null) continue;
                    var shortName = GetString(rec, "route_short_name") ?? string.Empty;
                    var longName = GetString(rec, "route_long_name") ?? string.Empty;
                    var routeName = shortName;
                    if (string.IsNullOrEmpty(routeName)) routeName = longName;
                    if (string.IsNullOrEmpty(routeName)) routeName = routeId;
                    if (string.IsNullOrEmpty(routeName)) routeName = "unknown";
                    var onestopId = _onestopId.GenerateRouteOnestopId(centerLat, centerLon, routeName);

                    if (allRoutesByOnestopId.TryGetValue(onestopId, out var route))
                        routeIdToRoute[routeId] = route;
                }

                foreach (var trip in trips)
                {
                    if (routeIdToRoute.TryGetValue(trip.RouteId, out var route))
                        trip.CanonicalRouteId = route.Id;
                }

                _db.Shapes.AddRange(shapes);

                var tripIdLookup = trips.ToDictionary(
                    static t => t.TripId, static t => t.Id, StringComparer.OrdinalIgnoreCase);

                var rawStRecords = GetTableRecords(tableRecords, "stop_times") ?? [];
                foreach (var (rawRecord, stopTime) in rawStRecords.Zip(stopTimes))
                {
                    var rawTripId = GetString(rawRecord, "trip_id");
                    if (rawTripId is not null &&
                        tripIdLookup.TryGetValue(rawTripId, out var tripId))
                        stopTime.TripId = tripId;
                }
                _db.StopTimes.AddRange(stopTimes);
                await _db.SaveChangesAsync(ct);

                BackfillRouteGeometries(trips, shapes, allRoutes);

                version.StopCount = stops.Count;
                version.RouteCount = allRoutes.Count;
                version.TripCount = trips.Count;
                version.AgencyCount = agency.Count;

                var prevActiveVersions = await _db.FeedVersions
                    .Where(fv => fv.FeedId == feed.Id && fv.IsActive && fv.Id != feedVersionId)
                    .ToListAsync(ct);
                foreach (var pv in prevActiveVersions)
                    pv.IsActive = false;

                version.IsActive = true;
                version.ImportStatus = FeedImportStatus.Success;
                version.ImportError = null;
                version.ImportedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "CustomFeed {CustomFeedId}: import complete — version {VersionId} active " +
                    "({RouteCount} routes, {StopCount} stops, {TripCount} trips)",
                    customFeed.Id, feedVersionId, allRoutes.Count, stops.Count, trips.Count);

                await PruneOldVersionsAsync(feed.Id, ct);
            }
            catch
            {
                // await using will roll back the transaction
                throw;
            }
        }
        finally
        {
            feedLock.Release();
        }

        if (customFeed.IsScheduleCapable)
        {
            try
            {
                var newActiveVersion = await _db.FeedVersions
                    .Where(fv => fv.FeedId == feed.Id && fv.IsActive)
                    .OrderByDescending(fv => fv.Id)
                    .FirstAsync(ct);
                await _reconciliation.ReconcileFeedVersionAsync(newActiveVersion.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Reconciliation failed after import for CustomFeed {CustomFeedId}",
                    customFeed.Id);
            }
        }
        else
        {
            _logger.LogInformation(
                "CustomFeed {CustomFeedId}: schedule-incapable, skipping reconciliation",
                customFeed.Id);
        }
    }

    private async Task PruneOldVersionsAsync(int feedId, CancellationToken ct)
    {
        var versionIds = await _db.FeedVersions
            .Where(fv => fv.FeedId == feedId)
            .OrderByDescending(fv => fv.Id)
            .Select(fv => fv.Id)
            .ToListAsync(ct);

        var retainCount = Math.Max(1, _importOptions.Value.MaxVersionRetention);
        var pruneIds = versionIds.Skip(retainCount).ToList();
        if (pruneIds.Count == 0) return;

        foreach (var vid in pruneIds)
        {
            await _db.StopTimes.Where(st => st.Trip.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.CalendarDates.Where(cd => cd.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.Calendars.Where(c => c.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.Shapes.Where(s => s.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.Trips.Where(t => t.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.ReconciliationCandidates.Where(rc => rc.RawStop.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.RawStops.Where(rs => rs.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.Agencies.Where(a => a.FeedVersionId == vid).ExecuteDeleteAsync(ct);
        }

        await _db.FeedVersions
            .Where(fv => pruneIds.Contains(fv.Id))
            .ExecuteDeleteAsync(ct);

        _logger.LogInformation("Pruned {Count} old FeedVersion(s) for feed {FeedId}", pruneIds.Count, feedId);
    }

    private bool ShouldAbortCutover(
        FeedVersion? activeVersion,
        List<RawStop> stops,
        List<CanonicalRoute> routes)
    {
        if (activeVersion is null) return false;

        var opt = _importOptions.Value;

        if (stops.Count == 0) return true;
        if (opt.StopCountDropThreshold > 0 && activeVersion.StopCount > 0)
        {
            var drop = 1.0 - (double)stops.Count / activeVersion.StopCount;
            if (drop > opt.StopCountDropThreshold) return true;
        }

        if (routes.Count == 0) return true;
        if (opt.RouteCountDropThreshold > 0 && activeVersion.RouteCount > 0)
        {
            var drop = 1.0 - (double)routes.Count / activeVersion.RouteCount;
            if (drop > opt.RouteCountDropThreshold) return true;
        }

        return false;
    }

    public int DeriveStopRouteTypes(
        List<RawStop> stops,
        List<Dictionary<string, object?>> stopTimeRecords,
        List<Dictionary<string, object?>> tripRecords,
        List<Dictionary<string, object?>> routeRecords)
    {
        var routeTypeByRouteId = new Dictionary<string, RouteType>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in routeRecords)
        {
            var routeId = GetString(record, "route_id");
            if (routeId is null) continue;
            var routeType = GetInt(record, "route_type");
            if (routeType.HasValue && !routeTypeByRouteId.ContainsKey(routeId))
                routeTypeByRouteId[routeId] = MapGtfsRouteType(routeType.Value);
        }

        var routeTypeByTripId = new Dictionary<string, RouteType>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in tripRecords)
        {
            var tripId = GetString(record, "trip_id");
            if (tripId is null) continue;
            var routeId = GetString(record, "route_id");
            if (routeId is null) continue;
            if (!routeTypeByRouteId.TryGetValue(routeId, out var rt)) continue;
            if (!routeTypeByTripId.ContainsKey(tripId))
                routeTypeByTripId[tripId] = rt;
        }

        var routeTypeByStopId = new Dictionary<string, RouteType>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in stopTimeRecords)
        {
            var stopId = GetString(record, "stop_id");
            if (stopId is null) continue;
            var tripId = GetString(record, "trip_id");
            if (tripId is null) continue;
            if (routeTypeByStopId.ContainsKey(stopId)) continue;
            if (!routeTypeByTripId.TryGetValue(tripId, out var rt)) continue;
            routeTypeByStopId[stopId] = rt;
        }

        var count = 0;
        foreach (var stop in stops)
        {
            if (routeTypeByStopId.TryGetValue(stop.RawStopId, out var rt))
            {
                stop.RouteType = rt;
                count++;
            }
            else
            {
                _logger.LogWarning("Stop {StopId} has no derivable route type; RouteType set to null",
                    stop.RawStopId);
            }
        }
        return count;
    }

    public ImportValidationResult ValidateImport(
        int stopsWithRouteType,
        List<Calendar>? calendar,
        List<CalendarDate>? calendarDates)
    {
        List<string> warnings = [];

        if (calendar is null || calendar.Count == 0)
        {
            if (calendarDates is null || calendarDates.Count == 0)
                warnings.Add("No calendar or calendar_dates — service schedule will be unavailable");
            else
                warnings.Add("No calendar — only calendar_dates will define service exceptions");
        }
        else if (calendarDates is null || calendarDates.Count == 0)
        {
            warnings.Add("No calendar_dates — no service exception dates defined");
        }

        return new ImportValidationResult
        {
            CanProceed = stopsWithRouteType > 0,
            Warnings = warnings
        };
    }

    private static string? GetString(Dictionary<string, object?> dict, string key)
    {
        return dict.TryGetValue(key, out var v) ? v?.ToString() : null;
    }

    private static double? GetDouble(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return null;
        if (v is double d) return d;
        if (v is int i) return i;
        if (v is long l) return l;
        if (double.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static int? GetInt(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return null;
        if (v is int i) return i;
        if (v is long l) return (int)l;
        if (int.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static bool? GetWheelchairBoarding(int? value) => value switch
    {
        1 => true,
        2 => false,
        _ => null
    };

    private static StationType MapStationType(int? locationType) => locationType switch
    {
        null => StationType.Stop,
        0 => StationType.Stop,
        1 => StationType.Station,
        2 => StationType.Platform,
        3 => StationType.Platform,
        4 => StationType.Platform,
        _ => StationType.Stop
    };

    private static RouteType MapGtfsRouteType(int gtfsType) => gtfsType switch
    {
        0 => RouteType.Tram,
        1 => RouteType.Subway,
        2 => RouteType.Train,
        3 => RouteType.Bus,
        4 => RouteType.Ferry,
        5 => RouteType.CableTram,
        6 => RouteType.CableCar,
        7 => RouteType.Funicular,
        11 => RouteType.Trolleybus,
        12 => RouteType.Monorail,
        100 => RouteType.Train,
        101 => RouteType.Train,
        102 => RouteType.Train,
        103 => RouteType.Train,
        104 => RouteType.Train,
        105 => RouteType.Train,
        106 => RouteType.Train,
        107 => RouteType.Train,
        108 => RouteType.Train,
        109 => RouteType.Train,
        200 => RouteType.Bus,
        201 => RouteType.Bus,
        202 => RouteType.Bus,
        203 => RouteType.Bus,
        204 => RouteType.Bus,
        400 => RouteType.Subway,
        401 => RouteType.Subway,
        402 => RouteType.Subway,
        403 => RouteType.Subway,
        404 => RouteType.Subway,
        405 => RouteType.Subway,
        700 => RouteType.Bus,
        701 => RouteType.Bus,
        702 => RouteType.Bus,
        703 => RouteType.Bus,
        704 => RouteType.Bus,
        705 => RouteType.Bus,
        715 => RouteType.Bus,
        800 => RouteType.Trolleybus,
        900 => RouteType.Tram,
        1000 => RouteType.Ferry,
        1100 => RouteType.Airplane,
        1200 => RouteType.Ferry,
        1300 => RouteType.CableCar,
        1400 => RouteType.Funicular,
        1500 => RouteType.Airplane,
        1700 => RouteType.Bus,
        _ => RouteType.Bus
    };

    private static bool? GetBoolFlag(int? value) => value.HasValue ? value == 1 : null;

    private static int? ParseGtfsTimeToSeconds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.Length == 7) raw = "0" + raw;
        var parts = raw.Split(':');
        if (parts.Length == 3
            && int.TryParse(parts[0], out var h)
            && int.TryParse(parts[1], out var m)
            && int.TryParse(parts[2], out var s))
            return h * 3600 + m * 60 + s;
        return null;
    }

    private static DateOnly? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateOnly.TryParseExact(raw, "yyyyMMdd", out var date)) return date;
        return null;
    }

    private static List<Dictionary<string, object?>>? GetTableRecords(
        Dictionary<string, List<Dictionary<string, object?>>> allRecords,
        string tableName)
    {
        return allRecords.TryGetValue(tableName, out var records) ? records : null;
    }

    public List<Shape> MapShapes(List<Dictionary<string, object?>> records)
    {
        if (records.Count == 0) return [];

        var pointsByShape = new Dictionary<string, List<(int Sequence, double Lat, double Lon)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var shapeId = GetString(record, "shape_id");
            if (string.IsNullOrEmpty(shapeId)) continue;

            var seq = GetInt(record, "shape_pt_sequence") ?? 0;
            var lat = GetDouble(record, "shape_pt_lat");
            var lon = GetDouble(record, "shape_pt_lon");

            if (lat is null || lon is null) continue;
            if (lat < -90 || lat > 90 || lon < -180 || lon > 180) continue;
            if (lat == 0.0 && lon == 0.0) continue;

            if (!pointsByShape.TryGetValue(shapeId, out var list))
                pointsByShape[shapeId] = list = [];
            list.Add((seq, lat.Value, lon.Value));
        }

        var shapes = new List<Shape>(pointsByShape.Count);
        foreach (var kvp in pointsByShape)
        {
            kvp.Value.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            if (kvp.Value.Count < 2) continue;

            var coords = kvp.Value.Select(p => new Coordinate(p.Lon, p.Lat)).ToArray();
            shapes.Add(new Shape
            {
                ShapeId = kvp.Key,
                Geometry = _shapeFactory.CreateLineString(coords),
                IsManuallyEdited = false
            });
        }

        _logger.LogInformation("Mapped {ShapeCount} shapes from {RecordCount} shape points",
            shapes.Count, records.Count);
        return shapes;
    }

    public List<Shape> AutoGenerateShapes(
        List<Trip> trips,
        List<Dictionary<string, object?>> rawStopTimes,
        List<RawStop> stops)
    {
        var stopsByStopId = new Dictionary<string, (double Lat, double Lon)>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stops)
            stopsByStopId[s.RawStopId] = (s.Lat, s.Lon);

        var stopTimesByTrip = new Dictionary<string, List<(string StopId, int Sequence)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in rawStopTimes)
        {
            var tripId = GetString(rec, "trip_id");
            if (string.IsNullOrEmpty(tripId)) continue;
            var stopId = GetString(rec, "stop_id");
            if (string.IsNullOrEmpty(stopId)) continue;
            var seq = GetInt(rec, "stop_sequence") ?? 0;

            if (!stopTimesByTrip.TryGetValue(tripId, out var list))
                stopTimesByTrip[tripId] = list = [];
            list.Add((stopId, seq));
        }

        foreach (var kvp in stopTimesByTrip)
            kvp.Value.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));

        var generatedCount = 0;
        var shapes = new List<Shape>(trips.Count);

        foreach (var trip in trips)
        {
            if (!stopTimesByTrip.TryGetValue(trip.TripId, out var tripStopTimes))
                continue;

            var coords = new List<Coordinate>(tripStopTimes.Count);
            foreach (var (stopId, _) in tripStopTimes)
            {
                if (!stopsByStopId.TryGetValue(stopId, out var stop)) continue;
                if (stop.Lat == 0.0 && stop.Lon == 0.0) continue;
                coords.Add(new Coordinate(stop.Lon, stop.Lat));
            }

            if (coords.Count < 2) continue;

            var shapeId = $"gen-{trip.TripId}";
            shapes.Add(new Shape
            {
                ShapeId = shapeId,
                Geometry = _shapeFactory.CreateLineString([.. coords]),
                IsManuallyEdited = false
            });
            trip.ShapeId = shapeId;
            generatedCount++;
        }

        _logger.LogInformation(
            "Auto-generated {GeneratedCount} shapes from stop_times for {TripCount} trips",
            generatedCount, trips.Count);
        return shapes;
    }

    private void BackfillRouteGeometries(
        List<Trip> trips,
        List<Shape> shapes,
        List<CanonicalRoute> routes)
    {
        var shapeLookup = shapes.ToDictionary(
            s => s.ShapeId, s => s, StringComparer.OrdinalIgnoreCase);

        var shapeCounts = trips
            .Where(t => t.CanonicalRouteId.HasValue && t.ShapeId is not null)
            .GroupBy(t => new { t.CanonicalRouteId, t.ShapeId })
            .Select(g => new { CanonicalRouteId = g.Key.CanonicalRouteId!.Value, ShapeId = g.Key.ShapeId!, Count = g.Count() })
            .ToList();

        var routeShapes = shapeCounts
            .GroupBy(g => g.CanonicalRouteId)
            .Select(g => new
            {
                RouteId = g.Key,
                MostCommonShapeId = g.OrderByDescending(x => x.Count).Select(x => x.ShapeId).FirstOrDefault()
            })
            .ToList();

        var routeLookup = routes.ToDictionary(r => r.Id);
        var backfilledCount = 0;

        foreach (var rs in routeShapes)
        {
            if (rs.MostCommonShapeId is null) continue;
            if (!routeLookup.TryGetValue(rs.RouteId, out var cr)) continue;
            if (!shapeLookup.TryGetValue(rs.MostCommonShapeId, out var sd)) continue;

            cr.Geometry = sd.Geometry;
            cr.ShapeEdited = sd.IsManuallyEdited;
            backfilledCount++;
        }

        _logger.LogInformation(
            "Backfilled geometries for {BackfilledCount} of {RouteCount} routes",
            backfilledCount, routes.Count);
    }
}
