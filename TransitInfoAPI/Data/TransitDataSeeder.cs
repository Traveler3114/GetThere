using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Data;

/// <summary>
/// Onboards the Croatian operator catalog from <c>docs/operator-data-sources.md</c>: every operator
/// gets an <see cref="Operator"/> row, and each one with a data source gets its GTFS (and, where
/// known, GTFS-RT) <see cref="Feed"/>s. Operators with no source at all are still seeded — the point
/// of the roster is that the whole catalogue exists, not just the part that imports.
/// <para>
/// Idempotent: every entity here is upserted on a natural key (<c>Operator.OnestopId</c>,
/// <c>Feed.FeedId</c>, <c>CustomSource.Name</c>), so re-running never duplicates rows. The GTFS-RT
/// feeds follow the historical <c>zet-2</c>/<c>gp-2</c> convention — the static feed owns the plain
/// slug and the realtime feed takes <c>{slug}-2</c>.
/// </para>
/// <para>
/// The NAP feeds (<c>b2b.promet-info.hr</c>) are activated up front and authenticate with HTTP
/// Basic credentials from user-secrets (<c>Feeds:BasicAuth:b2b.promet-info.hr</c>, see
/// <see cref="Services.ExternalFeedSource"/>). They import once the credential is set; without it
/// they 401 harmlessly.
/// </para>
/// </summary>
public sealed class TransitDataSeeder
{
    private readonly TransitDbContext _db;
    private readonly OnestopIdManager _onestopId;
    private readonly ILogger<TransitDataSeeder> _logger;

    public TransitDataSeeder(
        TransitDbContext db,
        OnestopIdManager onestopId,
        ILogger<TransitDataSeeder> logger)
    {
        _db = db;
        _onestopId = onestopId;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        foreach (var roster in Roster)
        {
            var op = await UpsertOperatorAsync(roster, ct);

            foreach (var feed in roster.Feeds)
                await UpsertFeedAsync(op, feed, roster.Provenance, ct);

            if (roster.CustomSource is not null)
            {
                if (roster.Slug == "flixbus")
                    await UpsertFlixbusSourceAsync(op, ct);
                else
                    await UpsertAutotrolejSourceAsync(op, ct);
            }
        }

        foreach (var seed in AlertSourceRoster)
            await UpsertAlertSourceAsync(seed, ct);

        await UpsertHzppInterpolateAsync(ct);

        await UpsertNextbikeAsync(ct);
    }

    // ── Roster ────────────────────────────────────────────────────────────────────────────────

    private const string GitLabBase = "https://gitlab.com/api/v4/projects/vekejsn%2Fgtfs-generators/packages/generic";
    private const string PirnetRt = "https://rt.gtfs.baguette.pirnet.si/gtfs-rt";
    private const string PirnetMiscRt = "https://rt-misc.ojpp-http.pirnet.si";
    private const string Hoermalmeister = "https://hoermalmeister.github.io/gtfs-rehost";

    // ── Autotrolej — JSON custom source (stops + routes) ──────────────────────────────────────
    // The operator's public timetable API is richer than GTFS (departures + live GPS), so it is
    // ingested as a custom source rather than a GTFS feed. The mappings are the verified shape of
    // the API: {"res": {"<key>": {…}}} — DataPath "res" yields one row per keyed property.
    // Declared before Roster: static fields initialize in declaration order, and Roster captures
    // this list by reference when the autotrolej entry is constructed.

    private static readonly IReadOnlyList<CustomSourceRequest> AutotrolejRequests =
    [
        new()
        {
            SortOrder = 1,
            TargetSection = TransitSection.Stops,
            Url = "https://api.autotrolej.hr/api/open/v1/voznired/stanice",
            HttpMethod = "GET",
            Format = CustomSourceFormat.Json,
            DataPath = "res",
            DistinctBy = "StopId",
            Mappings =
            [
                new CustomSourceMapping { SortOrder = 1, SourceExpression = "id", TargetField = "StopId", Kind = MappingKind.Direct },
                new CustomSourceMapping { SortOrder = 2, SourceExpression = "naziv", TargetField = "StopName", Kind = MappingKind.Direct },
                new CustomSourceMapping { SortOrder = 3, SourceExpression = "gpsY", TargetField = "StopLat", Kind = MappingKind.Direct },
                new CustomSourceMapping { SortOrder = 4, SourceExpression = "gpsX", TargetField = "StopLon", Kind = MappingKind.Direct }
            ]
        },
        new()
        {
            SortOrder = 2,
            TargetSection = TransitSection.Routes,
            Url = "https://api.autotrolej.hr/api/open/v1/voznired/linije",
            HttpMethod = "GET",
            Format = CustomSourceFormat.Json,
            DataPath = "res",
            DistinctBy = "RouteId",
            Mappings =
            [
                new CustomSourceMapping { SortOrder = 1, SourceExpression = "id", TargetField = "RouteId", Kind = MappingKind.Direct },
                new CustomSourceMapping { SortOrder = 2, SourceExpression = "brojLinije", TargetField = "RouteShortName", Kind = MappingKind.Direct },
                new CustomSourceMapping { SortOrder = 3, SourceExpression = "naziv", TargetField = "RouteLongName", Kind = MappingKind.Direct },
                new CustomSourceMapping { SortOrder = 4, SourceExpression = "3", TargetField = "RouteType", Kind = MappingKind.Static }
            ]
        }
    ];

    // FlixBus "Where Is My Ride" — JSON, so it is a custom source rather than a GTFS-RT feed.
    // One request per hub: each ride carries its whole call list, so a handful of hubs covers the
    // country, and DistinctBy collapses rides that appear at two of them.
    //
    // Seeded INACTIVE. flixbus.com/robots.txt disallows /track/station/, which is the surface this
    // reads, and docs/operator-data-sources.md marks FlixBus "Defer (ToS)". Activating it is a
    // deliberate act in the admin console — do not flip this default.
    private const string FlixbusKey = "7781b8fa-07cf-4ab7-8b62-1f3178523ba0";

    private static CustomSourceRequest FlixbusStation(int order, string stationUuid) => new()
    {
        SortOrder = order,
        TargetSection = TransitSection.Vehicles,
        Url = $"https://global.api.flixbus.com/gis/v2/timetable/{stationUuid}/departures"
            + $"?from={{now}}&to={{now+90m}}&apiKey={FlixbusKey}",
        HttpMethod = "GET",
        Format = CustomSourceFormat.Json,
        DataPath = "rides",
        DistinctBy = "VehicleId",
        Mappings =
        [
            new CustomSourceMapping { SortOrder = 1, SourceExpression = "id",                            TargetField = "VehicleId",       Kind = MappingKind.Direct },
            new CustomSourceMapping { SortOrder = 2, SourceExpression = "location.coordinates.latitude",  TargetField = "Latitude",        Kind = MappingKind.Direct },
            new CustomSourceMapping { SortOrder = 3, SourceExpression = "location.coordinates.longitude", TargetField = "Longitude",       Kind = MappingKind.Direct },
            new CustomSourceMapping { SortOrder = 4, SourceExpression = "location.updated_at",            TargetField = "LastUpdated",     Kind = MappingKind.Direct },
            new CustomSourceMapping { SortOrder = 5, SourceExpression = "line.code",                      TargetField = "RouteId",         Kind = MappingKind.Direct },
            new CustomSourceMapping { SortOrder = 6, SourceExpression = "line.code",                      TargetField = "RouteShortName",  Kind = MappingKind.Direct },
            new CustomSourceMapping { SortOrder = 7, SourceExpression = "location.speed_category",        TargetField = "CongestionLevel", Kind = MappingKind.Direct }
        ]
    };

    private static readonly IReadOnlyList<CustomSourceRequest> FlixbusRequests =
    [
        FlixbusStation(1, "dcbdbe76-9603-11e6-9066-549f350fcb0c"), // Zagreb (Bus Station)
        FlixbusStation(2, "dcc29b84-9603-11e6-9066-549f350fcb0c"), // Split bus station
        FlixbusStation(3, "dcc297e0-9603-11e6-9066-549f350fcb0c"), // Rijeka bus station
        FlixbusStation(4, "ed2bcf0b-5a7e-4780-b7bc-a2a2848741f5")  // Zagreb Airport
    ];

    private sealed record AlertSourceSeed(
        string SourceKey, string OperatorSlug, string Kind, string Format, string Url,
        string? ItemSelector = null, string? TitleSelector = null, string? DescriptionSelector = null,
        string? DateSelector = null, string? CategorySelector = null);

    private static readonly IReadOnlyList<AlertSourceSeed> AlertSourceRoster =
    [
        new("zet-izmjene", "zet", "Transit", "Html",
            "https://www.zet.hr/aktualnosti/izmjene-u-prometu/31",
            ItemSelector: "a[href*=\"/izmjene-u-prometu/\"]"),
        new("hzpp-info", "hzpp", "Transit", "Html",
            "https://www.hzpp.hr/hr/informacije?type=info",
            ItemSelector: "div.accordion-item.railway-works-accordion",
            TitleSelector: "button, .accordion-button, h3, h4",
            DescriptionSelector: "div.text-container",
            CategorySelector: "div.status"),
        new("autotrolej-obavijesti", "autotrolej", "Transit", "Html",
            "https://www.autotrolej.hr/obavijesti/",
            ItemSelector: "div.news-content",
            TitleSelector: "h3 a, h4 a", DescriptionSelector: "p",
            DateSelector: "div.news-meta", CategorySelector: "div.news-meta"),
        new("split-obavijesti", "promet-split", "Transit", "Html",
            "https://www.promet-split.hr/obavijesti/category/obavijesti",
            ItemSelector: "article.c-article-card",
            TitleSelector: "h2, h3, .c-article-card__title, a",
            DescriptionSelector: ".c-article-card__summary",
            DateSelector: ".c-article-card__date",
            CategorySelector: ".c-article-card__label"),
        new("osijek-promet", "gpp-osijek", "Transit", "Html",
            "https://web.gpp-osijek.com/kategorija/promet/",
            ItemSelector: "div.entry-main",
            TitleSelector: "h2 a, h3 a, .entry-title a",
            DescriptionSelector: ".entry-content",
            DateSelector: ".entry-date", CategorySelector: ".entry-category"),
        new("pulapromet-novosti", "pulapromet", "Transit", "Html",
            "https://www.pulapromet.hr/novosti",
            ItemSelector: "a[href*=\"/novosti/detaljnije/\"]"),
        new("jadrolinija-notices", "jadrolinija", "Transit", "Html",
            "https://www.jadrolinija.hr/en/user-notifications;https://www.jadrolinija.hr/news-single/stanje-u-pomorskom-prometu",
            ItemSelector: "article.card",
            TitleSelector: ".card__title, h3, h2, a",
            DescriptionSelector: ".card__data, p, .card__text",
            CategorySelector: ".card__label"),
        new("hak-events", "hak", "Road", "GeoJson",
            "https://b2b.promet-info.hr/dc/b2b.hak.events.geojson.hr_HR",
            ItemSelector: "features"),
        new("hak-roadworks", "hak", "Road", "GeoJson",
            "https://b2b.promet-info.hr/dc/b2b.hak.roadworks.geojson.hr_HR",
            ItemSelector: "features")
    ];

    /// <summary>One feed of one operator. The static feed keeps the slug; GTFS-RT takes <c>{slug}-2</c>.</summary>
    private sealed record FeedSeed(string FeedId, FeedType FeedType, string? Url, bool IsActive = true);

    private sealed record OperatorSeed(
        string Slug,
        string Name,
        SourceProvenance Provenance,
        IReadOnlyList<FeedSeed> Feeds,
        IReadOnlyList<CustomSourceRequest>? CustomSource = null);

    private static readonly IReadOnlyList<OperatorSeed> Roster =
    [
        // ── Real-data operators, in the order docs/operator-data-sources.md lists them ──────────
        new("zet", "Zagrebački električni tramvaj", SourceProvenance.Official,
        [
            new("zet", FeedType.GTFSStatic, "https://zet.hr/gtfs-scheduled/latest"),
            new("zet-2", FeedType.GTFSRealtime, "https://zet.hr/gtfs-rt-protobuf")
        ]),
        new("hzpp", "HŽ Putnički prijevoz", SourceProvenance.Official,
        [
            // The old hzpp.hr URL was a pinned repository GUID: it downloads successfully every poll
            // and always returns the same file, whose calendar ended 2025-12-14 — so every HŽPP
            // service read as "not running today" and rail dropped out of routing entirely. The NAP
            // feed is the maintained official one (HTTP Basic, Feeds:BasicAuth:b2b.promet-info.hr).
            new("hzpp", FeedType.GTFSStatic, "https://b2b.promet-info.hr/dc/b2b.gtfs.hz"),
            new("hzpp-2", FeedType.GTFSRealtime, $"{PirnetRt}/HZPP/trip_updates.pb")
        ]),
        new("sibenik", "Gradski parking Šibenik", SourceProvenance.Official,
        [
            new("sibenik", FeedType.GTFSStatic, "https://www.gradski-parking.hr/upload/stranice/2022/08/2022-08-30/89/gtfs.zip"),
            new("sibenik-2", FeedType.GTFSRealtime, $"{PirnetMiscRt}/sibenik/trip-updates.pb")
        ]),
        new("autotrolej", "Autotrolej Rijeka", SourceProvenance.Official,
        [
            new("autotrolej-2", FeedType.GTFSRealtime, $"{PirnetMiscRt}/autotrolej/trip-updates.pb")
        ], CustomSource: AutotrolejRequests),
        new("gpp-osijek", "GPP Osijek", SourceProvenance.Official,
        [
            new("gpp-osijek", FeedType.GTFSStatic, "https://b2b.promet-info.hr/dc/b2b.gtfs.osijekgpp")
        ]),
        new("pulapromet", "Pulapromet", SourceProvenance.Official,
        [
            new("pulapromet", FeedType.GTFSStatic, "https://b2b.promet-info.hr/dc/b2b.gtfs.pulapromet"),
            new("pulapromet-2", FeedType.GTFSRealtime, $"{PirnetMiscRt}/pulapromet/trip-updates.pb")
        ]),
        new("jadrolinija", "Jadrolinija", SourceProvenance.Official,
        [
            new("jadrolinija", FeedType.GTFSStatic, "https://b2b.promet-info.hr/dc/b2b.gtfs.jl")
        ]),
        new("autotransport-karlovac", "Autotransport Karlovac", SourceProvenance.Official,
        [
            new("autotransport-karlovac", FeedType.GTFSStatic, "https://b2b.promet-info.hr/dc/b2b.gtfs.karlovac")
        ]),
        new("promet-split", "Promet Split", SourceProvenance.UnofficialMirror,
        [
            new("promet-split", FeedType.GTFSStatic, $"{GitLabBase}/split-gtfs/latest/split_gtfs.zip"),
            new("promet-split-2", FeedType.GTFSRealtime, $"{PirnetRt}/prometSplit/trip_updates.pb")
        ]),
        new("liburnija-zadar", "Liburnija Zadar", SourceProvenance.UnofficialMirror,
        [
            new("liburnija-zadar", FeedType.GTFSStatic, $"{GitLabBase}/zadar-gtfs/latest/zadar_gtfs.zip"),
            new("liburnija-zadar-2", FeedType.GTFSRealtime, $"{PirnetRt}/liburnijaZadar/trip_updates.pb"),
            new("liburnija-zadar-3", FeedType.GTFSRealtime, $"{PirnetRt}/liburnijaZadar/vehicle_positions.pb")
        ]),
        new("libertas-dubrovnik", "Libertas Dubrovnik", SourceProvenance.UnofficialMirror,
        [
            new("libertas-dubrovnik", FeedType.GTFSStatic, $"{GitLabBase}/libertas-gtfs/latest/libertas_gtfs.zip")
        ]),
        new("ap-sisak", "AP Sisak", SourceProvenance.UnofficialMirror,
        [
            new("ap-sisak", FeedType.GTFSStatic, $"{GitLabBase}/sisak-gtfs/latest/ap_sisak_gtfs.zip"),
            new("ap-sisak-2", FeedType.GTFSRealtime, $"{PirnetRt}/apSisak/trip_updates.pb"),
            new("ap-sisak-3", FeedType.GTFSRealtime, $"{PirnetRt}/apSisak/vehicle_positions.pb")
        ]),
        new("crikvenica", "Crikvenica", SourceProvenance.UnofficialMirror,
        [
            new("crikvenica", FeedType.GTFSStatic, $"{Hoermalmeister}/crikvenica/crikvenica.zip")
        ]),
        new("opatija", "Opatija", SourceProvenance.UnofficialMirror,
        [
            new("opatija", FeedType.GTFSStatic, $"{Hoermalmeister}/opatija/opatija.zip")
        ]),
        new("porec", "Poreč", SourceProvenance.UnofficialMirror,
        [
            new("porec", FeedType.GTFSStatic, $"{Hoermalmeister}/porec/porec.zip")
        ]),
        new("sveta-nedelja", "Sveta Nedelja", SourceProvenance.UnofficialMirror,
        [
            new("sveta-nedelja", FeedType.GTFSStatic, $"{Hoermalmeister}/sveta_nedelja/sveta_nedelja.zip")
        ]),
        new("vela-luka", "Vela Luka", SourceProvenance.UnofficialMirror,
        [
            new("vela-luka", FeedType.GTFSStatic, $"{Hoermalmeister}/vela_luka/vela%20luka.zip")
        ]),
        new("terzic-slavonski-brod", "Terzić", SourceProvenance.UnofficialMirror,
        [
            new("terzic-slavonski-brod", FeedType.GTFSStatic, $"{Hoermalmeister}/slavonski_brod/slavonski_brod.zip")
        ]),
        new("rapska-plovidba", "Rapska plovidba", SourceProvenance.UnofficialMirror,
        [
            new("rapska-plovidba", FeedType.GTFSStatic, $"{Hoermalmeister}/rapska_plovidba/rapska_plovidba.zip")
        ]),
        new("rapska-vozidba", "Rapska vozidba", SourceProvenance.UnofficialMirror,
        [
            new("rapska-vozidba", FeedType.GTFSStatic, "https://owncloud.cesnet.cz/index.php/s/UudJhpom7fgur2X/download")
        ]),

        // ── No data source — the Operator row exists so the catalogue is complete ──────────────
        new("arriva-autotrans", "Arriva Autotrans", SourceProvenance.Official, []),
        new("cazmatrans", "Čazmatrans", SourceProvenance.Official, []),
        new("krilo", "Krilo (Kapetan Luka)", SourceProvenance.Official, []),
        new("tp-line", "TP Line", SourceProvenance.Official, []),
        new("gv-line", "G&V Line", SourceProvenance.Official, []),
        new("bura-line", "Bura Line", SourceProvenance.Official, []),
        new("polet-vinkovci", "Polet Vinkovci", SourceProvenance.Official, []),
        new("slavonija-bus", "Slavonija Bus", SourceProvenance.Official, []),
        new("panturist", "Panturist", SourceProvenance.Official, []),
        new("samoborcek", "Samoborček", SourceProvenance.Official, []),
        new("vincek", "Vincek", SourceProvenance.Official, []),
        new("presecki-grupa", "Presečki Grupa", SourceProvenance.Official, []),
        new("ap-varazdin", "Autobusni prijevoz Varaždin", SourceProvenance.Official, []),
        new("brioni", "Brioni", SourceProvenance.Official, []),
        // Hrvatski autoklub — publishes the national road-event and roadworks GeoJSON via the NAP.
        // It owns no transit feeds; it exists so its road alerts have an operator, because
        // Feed.OperatorId is non-nullable.
        new("hak", "Hrvatski autoklub", SourceProvenance.Official, []),
        new("flixbus", "FlixBus", SourceProvenance.ReverseEngineered,
        [
            new("flixbus-2", FeedType.GTFSStatic, null, IsActive: false)
        ], CustomSource: FlixbusRequests),
    ];

    // ── Shared upserts ────────────────────────────────────────────────────────────────────────

    private async Task<Operator> UpsertOperatorAsync(OperatorSeed roster, CancellationToken ct)
    {
        var onestopId = _onestopId.GenerateOperatorOnestopId(roster.Slug);
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.OnestopId == onestopId, ct);
        if (op is not null)
            return op;

        op = new Operator
        {
            GlobalId = "gt-" + roster.Slug,
            OnestopId = onestopId,
            Name = roster.Name,
            ShortName = roster.Slug,
            CreatedAt = DateTime.UtcNow
        };
        _db.Operators.Add(op);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created operator '{OnestopId}' ({Name})", onestopId, roster.Name);
        return op;
    }

    private async Task UpsertFeedAsync(Operator op, FeedSeed seed, SourceProvenance provenance, CancellationToken ct, int? customSourceId = null)
    {
        var existing = await _db.Feeds.FirstOrDefaultAsync(f => f.FeedId == seed.FeedId, ct);
        if (existing is not null)
        {
            if (existing.Url != seed.Url)
            {
                _logger.LogInformation("Repairing feed '{FeedId}': URL changed from '{Old}' to '{New}'", seed.FeedId, existing.Url, seed.Url);
                existing.Url = seed.Url;
                await _db.SaveChangesAsync(ct);
            }
            return;
        }

        _db.Feeds.Add(new Feed
        {
            OnestopId = _onestopId.GenerateFeedOnestopId(0, 0, seed.FeedId),
            FeedId = seed.FeedId,
            FeedType = seed.FeedType,
            Url = seed.Url,
            IsActive = seed.IsActive,
            RefreshIntervalSeconds = 3600,
            OperatorId = op.Id,
            CustomSourceId = customSourceId,
            Provenance = provenance
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created feed '{FeedId}' for operator '{OnestopId}'", seed.FeedId, op.OnestopId);
    }

    private async Task UpsertAutotrolejSourceAsync(Operator op, CancellationToken ct)
    {
        var source = await UpsertCustomSourceAsync(op.Id, "autotrolej", AutotrolejRequests, ct);

        var feedExists = await _db.Feeds.AnyAsync(f => f.FeedId == "autotrolej", ct);
        if (!feedExists)
        {
            _db.Feeds.Add(new Feed
            {
                OnestopId = _onestopId.GenerateFeedOnestopId(0, 0, "autotrolej"),
                FeedId = "autotrolej",
                FeedType = FeedType.GTFSStatic,
                IsActive = true,
                RefreshIntervalSeconds = 3600,
                OperatorId = op.Id,
                CustomSourceId = source.Id,
                Provenance = SourceProvenance.Official
            });
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Created feed 'autotrolej' backed by custom source {SourceId}", source.Id);
        }
    }

    private async Task UpsertFlixbusSourceAsync(Operator op, CancellationToken ct)
    {
        var source = await UpsertCustomSourceAsync(op.Id, "flixbus", FlixbusRequests, ct, producesRealtime: true);
        // Ensure feed flixbus-2 is linked to the realtime custom source
        var feed = await _db.Feeds.FirstOrDefaultAsync(f => f.FeedId == "flixbus-2", ct);
        if (feed is not null)
        {
            if (feed.CustomSourceId != source.Id || feed.IsActive != false)
            {
                feed.CustomSourceId = source.Id;
                feed.IsActive = false;
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Linked feed 'flixbus-2' to realtime custom source {SourceId}", source.Id);
            }
        }
        else
        {
            _db.Feeds.Add(new Feed
            {
                OnestopId = _onestopId.GenerateFeedOnestopId(0, 0, "flixbus-2"),
                FeedId = "flixbus-2",
                FeedType = FeedType.GTFSStatic,
                IsActive = false,
                RefreshIntervalSeconds = 3600,
                OperatorId = op.Id,
                CustomSourceId = source.Id,
                Provenance = SourceProvenance.ReverseEngineered
            });
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Created feed 'flixbus-2' backed by realtime custom source {SourceId}", source.Id);
        }
    }

    private async Task UpsertHzppInterpolateAsync(CancellationToken ct)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == "gt-hzpp", ct);
        if (op is null) return;

        var source = await UpsertCustomSourceAsync(op.Id, "hzpp-3", [], ct, producesRealtime: true, extractorKey: "gtfs-interpolate", authConfig: """{"sourceFeedId":"hzpp-2"}""");

        var feed = await _db.Feeds.FirstOrDefaultAsync(f => f.FeedId == "hzpp-3", ct);
        if (feed is null)
        {
            _db.Feeds.Add(new Feed
            {
                OnestopId = _onestopId.GenerateFeedOnestopId(0, 0, "hzpp-3"),
                FeedId = "hzpp-3",
                FeedType = FeedType.GTFSRealtime,
                IsActive = true,
                RefreshIntervalSeconds = 30,
                OperatorId = op.Id,
                CustomSourceId = source.Id,
                Provenance = SourceProvenance.Official
            });
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Created feed 'hzpp-3' interpolated realtime for operator '{OnestopId}'", op.OnestopId);
        }
        else
        {
            if (feed.CustomSourceId != source.Id || feed.OperatorId != op.Id)
            {
                feed.CustomSourceId = source.Id;
                feed.OperatorId = op.Id;
                await _db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task UpsertAlertSourceAsync(AlertSourceSeed seed, CancellationToken ct)
    {
        var existing = await _db.AlertSources.FirstOrDefaultAsync(a => a.SourceKey == seed.SourceKey, ct);
        if (existing is not null) return;

        var op = await _db.Operators.FirstOrDefaultAsync(o => o.GlobalId == "gt-" + seed.OperatorSlug, ct);
        if (op is null)
        {
            _logger.LogWarning("Alert source '{SourceKey}' operator 'gt-{Slug}' not found — skipping", seed.SourceKey, seed.OperatorSlug);
            return;
        }

        var alertSource = new AlertSource
        {
            SourceKey = seed.SourceKey,
            Kind = seed.Kind,
            Format = seed.Format,
            Url = seed.Url,
            ItemSelector = seed.ItemSelector,
            TitleSelector = seed.TitleSelector,
            DescriptionSelector = seed.DescriptionSelector,
            DateSelector = seed.DateSelector,
            CategorySelector = seed.CategorySelector,
            IntervalMinutes = 15,
            CreatedAt = DateTime.UtcNow
        };
        _db.AlertSources.Add(alertSource);
        await _db.SaveChangesAsync(ct);

        _db.Feeds.Add(new Feed
        {
            OnestopId = _onestopId.GenerateFeedOnestopId(0, 0, seed.SourceKey),
            FeedId = seed.SourceKey,
            FeedType = FeedType.AlertSource,
            IsActive = true,
            RefreshIntervalSeconds = 900,
            OperatorId = op.Id,
            AlertSourceId = alertSource.Id,
            Provenance = SourceProvenance.Official
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created alert source '{SourceKey}' for operator '{OnestopId}'", seed.SourceKey, op.OnestopId);
    }

    private async Task<CustomSource> UpsertCustomSourceAsync(
        int operatorId,
        string name,
        IReadOnlyList<CustomSourceRequest> requests,
        CancellationToken ct,
        bool producesRealtime = false,
        string? extractorKey = null,
        string? authConfig = null)
    {
        var source = await _db.CustomSources
            .Include(cs => cs.Requests).ThenInclude(r => r.Mappings)
            .AsSplitQuery()
            .FirstOrDefaultAsync(cs => cs.Name == name, ct);

        if (source is null)
        {
            source = new CustomSource
            {
                OperatorId = operatorId,
                Name = name,
                Kind = CustomSourceKind.Http,
                RefreshIntervalSeconds = 3600,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                ProducesRealtime = producesRealtime,
                ExtractorKey = extractorKey,
                AuthConfig = authConfig
            };
            foreach (var request in requests)
                source.Requests.Add(CopyRequest(request));
            _db.CustomSources.Add(source);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Created custom source '{Name}' for operator {OperatorId}", name, operatorId);
            return source;
        }

        // Existing source: repair drift. Requests are replaced wholesale whenever they do not match
        // the sealed definition — this is what fixes a stale localhost test source for Autotrolej
        // on a database seeded before the real URLs existed.
        var needsRepair = source.OperatorId != operatorId || source.ProducesRealtime != producesRealtime || source.ExtractorKey != extractorKey || source.AuthConfig != authConfig || !ShapedRequestsMatch(source, requests);
        if (!needsRepair) return source;

        _logger.LogInformation("Repairing custom source '{Name}': replacing requests and reconciling its definition", name);
        source.OperatorId = operatorId;
        source.ProducesRealtime = producesRealtime;
        source.ExtractorKey = extractorKey;
        source.AuthConfig = authConfig;
        _db.CustomSourceMappings.RemoveRange(source.Requests.SelectMany(r => r.Mappings));
        _db.CustomSourceRequests.RemoveRange(source.Requests);
        source.Requests.Clear();
        foreach (var request in requests)
            source.Requests.Add(CopyRequest(request));
        await _db.SaveChangesAsync(ct);
        return source;
    }

    private static CustomSourceRequest CopyRequest(CustomSourceRequest request) => new()
    {
        SortOrder = request.SortOrder,
        TargetSection = request.TargetSection,
        Url = request.Url,
        HttpMethod = request.HttpMethod,
        Format = request.Format,
        DataPath = request.DataPath,
        DistinctBy = request.DistinctBy,
        Mappings = [.. request.Mappings.Select(m => new CustomSourceMapping
        {
            SortOrder = m.SortOrder,
            SourceExpression = m.SourceExpression,
            TargetField = m.TargetField,
            Kind = m.Kind
        })]
    };

    private static bool ShapedRequestsMatch(CustomSource source, IReadOnlyList<CustomSourceRequest> expected)
    {
        if (source.Requests.Count != expected.Count) return false;

        var actual = source.Requests.OrderBy(r => r.SortOrder).ToList();
        for (var i = 0; i < expected.Count; i++)
        {
            var a = actual[i];
            var e = expected[i];
            if (a.TargetSection != e.TargetSection
                || a.Url != e.Url
                || a.DataPath != e.DataPath
                || a.DistinctBy != e.DistinctBy
                || a.Format != e.Format)
                return false;

            var aMappings = a.Mappings.OrderBy(m => m.SortOrder).ToList();
            var eMappings = e.Mappings.OrderBy(m => m.SortOrder).ToList();
            if (aMappings.Count != eMappings.Count) return false;
            for (var j = 0; j < eMappings.Count; j++)
            {
                if (aMappings[j].SourceExpression != eMappings[j].SourceExpression
                    || aMappings[j].TargetField != eMappings[j].TargetField
                    || aMappings[j].Kind != eMappings[j].Kind)
                    return false;
            }
        }
        return true;
    }

    // ── Nextbike — declarative mobility via nested DataPath ───────────────────────────
    // No C# extractor: URL + DataPath "countries.cities.places" + direct mappings.
    // The generic nested-array flattening in CustomSourceEngine.ParseJsonRows makes
    // this declareable; previously it required a named ICustomExtractor.
    private static readonly IReadOnlyList<CustomSourceRequest> NextbikeRequests =
    [
        new()
        {
            SortOrder = 1,
            TargetSection = TransitSection.MobilityStations,
            Url = "https://api.nextbike.net/maps/nextbike-live.json",
            HttpMethod = "GET",
            Format = CustomSourceFormat.Json,
            DataPath = "countries.cities.places",
            DistinctBy = "station_id",
            Mappings =
            [
                new CustomSourceMapping { SortOrder = 1, SourceExpression = "uid", TargetField = "station_id", Kind = MappingKind.Direct },
                new CustomSourceMapping { SortOrder = 2, SourceExpression = "name", TargetField = "name", Kind = MappingKind.Direct },
                new CustomSourceMapping { SortOrder = 3, SourceExpression = "lat", TargetField = "lat", Kind = MappingKind.Direct },
                new CustomSourceMapping { SortOrder = 4, SourceExpression = "lng", TargetField = "lon", Kind = MappingKind.Direct },
                new CustomSourceMapping { SortOrder = 5, SourceExpression = "bikes", TargetField = "num_bikes_available", Kind = MappingKind.Direct },
                new CustomSourceMapping { SortOrder = 6, SourceExpression = "bike_racks", TargetField = "capacity", Kind = MappingKind.Direct }
            ]
        }
    ];

    private async Task UpsertNextbikeAsync(CancellationToken ct)
    {
        var slug = "nextbike";
        var onestopId = _onestopId.GenerateOperatorOnestopId(slug);
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.OnestopId == onestopId, ct);
        if (op is null)
        {
            op = new Operator
            {
                GlobalId = "gt-" + slug,
                OnestopId = onestopId,
                Name = "Nextbike",
                ShortName = slug,
                CreatedAt = DateTime.UtcNow
            };
            _db.Operators.Add(op);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Created operator '{OnestopId}' ({Name})", onestopId, "Nextbike");
        }

        var source = await UpsertCustomSourceAsync(op.Id, "nextbike", NextbikeRequests, ct);
        // Enforce mobility flag (UpsertCustomSourceAsync does not set it).
        if (!source.ProducesMobility || source.ExtractorKey is not null || source.RefreshIntervalSeconds != 120)
        {
            _logger.LogInformation("Repairing custom source 'nextbike' mobility flag/extractor");
            source.ProducesMobility = true;
            source.ExtractorKey = null;
            source.RefreshIntervalSeconds = 120;
            source.Kind = CustomSourceKind.Http;
            await _db.SaveChangesAsync(ct);
        }

        // Mobility custom source is polled directly via MobilityPollingWorker scanning CustomSources.
        // A feed row is still created for visibility and to keep the “custom source → feed” invariant
        // the admin console expects, but it is inert: FeedManager excludes mobility sources.
        var feedExists = await _db.Feeds.AnyAsync(f => f.FeedId == "nextbike", ct);
        if (!feedExists)
        {
            _db.Feeds.Add(new Feed
            {
                OnestopId = _onestopId.GenerateFeedOnestopId(0, 0, "nextbike"),
                FeedId = "nextbike",
                FeedType = FeedType.GBFS,
                Url = null,
                IsActive = true,
                RefreshIntervalSeconds = 120,
                OperatorId = op.Id,
                CustomSourceId = source.Id,
                Provenance = SourceProvenance.Official
            });
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Created feed 'nextbike' backed by custom source {SourceId}", source.Id);
        }
        else
        {
            var feed = await _db.Feeds.FirstOrDefaultAsync(f => f.FeedId == "nextbike", ct);
            if (feed is not null && feed.CustomSourceId != source.Id)
            {
                feed.CustomSourceId = source.Id;
                await _db.SaveChangesAsync(ct);
            }
        }
    }
}
