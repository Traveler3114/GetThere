using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsoCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Continent = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Places",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdmCountryCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdmRegionCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Lat = table.Column<double>(type: "float", nullable: false),
                    Lon = table.Column<double>(type: "float", nullable: false),
                    Population = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Places", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    CountryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cities_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Operators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GlobalId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OnestopId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Website = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OperatorType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false),
                    IsVirtual = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupersedesIds = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WikidataId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssociatedFeeds = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CountryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Operators_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CountryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Regions_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CanonicalStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GlobalId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OnestopId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    StationType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrimaryRouteType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParentStationId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupersedesIds = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Geometry = table.Column<Point>(type: "geography", nullable: true),
                    AdmCountryCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdmRegionCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    CityId = table.Column<int>(type: "int", nullable: true),
                    PlaceId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanonicalStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CanonicalStations_CanonicalStations_ParentStationId",
                        column: x => x.ParentStationId,
                        principalTable: "CanonicalStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CanonicalStations_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CanonicalStations_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CanonicalStations_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CanonicalRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GlobalId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OnestopId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LongName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RouteType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TextColor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SupersedesIds = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Geometry = table.Column<Geometry>(type: "geography", nullable: true),
                    OperatorId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanonicalRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CanonicalRoutes_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Feeds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OnestopId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FeedType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExternalUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InternalUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FeedId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RefreshIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LicenseName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LicenseUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LicenseCommercialUseAllowed = table.Column<bool>(type: "bit", nullable: true),
                    LicenseShareAlikeOptional = table.Column<bool>(type: "bit", nullable: true),
                    LicenseRedistributionAllowed = table.Column<bool>(type: "bit", nullable: true),
                    SupersedesIds = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OperatorId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feeds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Feeds_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MobilityProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedFormat = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExternalUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InternalUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConverterConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OperatorId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilityProviders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MobilityProviders_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CanonicalStationOperators",
                columns: table => new
                {
                    CanonicalStationId = table.Column<int>(type: "int", nullable: false),
                    OperatorId = table.Column<int>(type: "int", nullable: false),
                    PlatformInfo = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanonicalStationOperators", x => new { x.CanonicalStationId, x.OperatorId });
                    table.ForeignKey(
                        name: "FK_CanonicalStationOperators_CanonicalStations_CanonicalStationId",
                        column: x => x.CanonicalStationId,
                        principalTable: "CanonicalStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CanonicalStationOperators_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedId = table.Column<int>(type: "int", nullable: false),
                    HeaderText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DescriptionText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Cause = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Effect = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActivePeriodStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActivePeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AffectedStopIds = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AffectedRouteIds = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AffectedTripIds = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AffectedAgencyIds = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alerts_Feeds_FeedId",
                        column: x => x.FeedId,
                        principalTable: "Feeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FeedVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedId = table.Column<int>(type: "int", nullable: false),
                    Sha1 = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ImportStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConvexHull = table.Column<Geometry>(type: "geography", nullable: true),
                    ServiceLevelStart = table.Column<DateOnly>(type: "date", nullable: true),
                    ServiceLevelEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    StopCount = table.Column<int>(type: "int", nullable: false),
                    RouteCount = table.Column<int>(type: "int", nullable: false),
                    TripCount = table.Column<int>(type: "int", nullable: false),
                    AgencyCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedVersions_Feeds_FeedId",
                        column: x => x.FeedId,
                        principalTable: "Feeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MobilityStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    AvailableVehicles = table.Column<int>(type: "int", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MobilityProviderId = table.Column<int>(type: "int", nullable: false),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    CityId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilityStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MobilityStations_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MobilityStations_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MobilityStations_MobilityProviders_MobilityProviderId",
                        column: x => x.MobilityProviderId,
                        principalTable: "MobilityProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Agencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedVersionId = table.Column<int>(type: "int", nullable: false),
                    AgencyId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timezone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FareUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OperatorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Agencies_FeedVersions_FeedVersionId",
                        column: x => x.FeedVersionId,
                        principalTable: "FeedVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Agencies_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CalendarDates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedVersionId = table.Column<int>(type: "int", nullable: false),
                    ServiceId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ExceptionType = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarDates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarDates_FeedVersions_FeedVersionId",
                        column: x => x.FeedVersionId,
                        principalTable: "FeedVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Calendars",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedVersionId = table.Column<int>(type: "int", nullable: false),
                    ServiceId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Monday = table.Column<bool>(type: "bit", nullable: false),
                    Tuesday = table.Column<bool>(type: "bit", nullable: false),
                    Wednesday = table.Column<bool>(type: "bit", nullable: false),
                    Thursday = table.Column<bool>(type: "bit", nullable: false),
                    Friday = table.Column<bool>(type: "bit", nullable: false),
                    Saturday = table.Column<bool>(type: "bit", nullable: false),
                    Sunday = table.Column<bool>(type: "bit", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calendars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Calendars_FeedVersions_FeedVersionId",
                        column: x => x.FeedVersionId,
                        principalTable: "FeedVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawStops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedVersionId = table.Column<int>(type: "int", nullable: false),
                    RawStopId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Lat = table.Column<double>(type: "float", nullable: false),
                    Lon = table.Column<double>(type: "float", nullable: false),
                    StationType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParentRawStopId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StopCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StopDesc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZoneId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlatformCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WheelchairBoarding = table.Column<bool>(type: "bit", nullable: true),
                    RouteType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CanonicalStationId = table.Column<int>(type: "int", nullable: true),
                    ReconciliationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawStops_CanonicalStations_CanonicalStationId",
                        column: x => x.CanonicalStationId,
                        principalTable: "CanonicalStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RawStops_FeedVersions_FeedVersionId",
                        column: x => x.FeedVersionId,
                        principalTable: "FeedVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Shapes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedVersionId = table.Column<int>(type: "int", nullable: false),
                    ShapeId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Geometry = table.Column<LineString>(type: "geography", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shapes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shapes_FeedVersions_FeedVersionId",
                        column: x => x.FeedVersionId,
                        principalTable: "FeedVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Trips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedVersionId = table.Column<int>(type: "int", nullable: false),
                    TripId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RouteId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ServiceId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TripHeadsign = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TripShortName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DirectionId = table.Column<int>(type: "int", nullable: true),
                    ShapeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WheelchairAccessible = table.Column<bool>(type: "bit", nullable: true),
                    BikesAllowed = table.Column<bool>(type: "bit", nullable: true),
                    CanonicalRouteId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trips_CanonicalRoutes_CanonicalRouteId",
                        column: x => x.CanonicalRouteId,
                        principalTable: "CanonicalRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trips_FeedVersions_FeedVersionId",
                        column: x => x.FeedVersionId,
                        principalTable: "FeedVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RawStopId = table.Column<int>(type: "int", nullable: false),
                    RawStopName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawStopLat = table.Column<double>(type: "float", nullable: false),
                    RawStopLon = table.Column<double>(type: "float", nullable: false),
                    RawRouteType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CanonicalRouteType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NameMatched = table.Column<bool>(type: "bit", nullable: false),
                    DistanceMatched = table.Column<bool>(type: "bit", nullable: false),
                    RouteTypeMatched = table.Column<bool>(type: "bit", nullable: false),
                    AutoReconciled = table.Column<bool>(type: "bit", nullable: false),
                    SuggestedCanonicalStationId = table.Column<int>(type: "int", nullable: true),
                    ConfidenceScore = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    DistanceMeters = table.Column<decimal>(type: "decimal(14,4)", precision: 14, scale: 4, nullable: false),
                    NameSimilarityScore = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewedByAdminId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FeedId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciliationCandidates_CanonicalStations_SuggestedCanonicalStationId",
                        column: x => x.SuggestedCanonicalStationId,
                        principalTable: "CanonicalStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationCandidates_Feeds_FeedId",
                        column: x => x.FeedId,
                        principalTable: "Feeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationCandidates_RawStops_RawStopId",
                        column: x => x.RawStopId,
                        principalTable: "RawStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StopTimes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    RawStopId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RawStopEntityId = table.Column<int>(type: "int", nullable: true),
                    CanonicalStationId = table.Column<int>(type: "int", nullable: true),
                    ArrivalTime = table.Column<int>(type: "int", nullable: false),
                    DepartureTime = table.Column<int>(type: "int", nullable: false),
                    StopSequence = table.Column<int>(type: "int", nullable: false),
                    StopHeadsign = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PickupType = table.Column<int>(type: "int", nullable: true),
                    DropOffType = table.Column<int>(type: "int", nullable: true),
                    Timepoint = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StopTimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StopTimes_CanonicalStations_CanonicalStationId",
                        column: x => x.CanonicalStationId,
                        principalTable: "CanonicalStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StopTimes_RawStops_RawStopEntityId",
                        column: x => x.RawStopEntityId,
                        principalTable: "RawStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StopTimes_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Agencies_FeedVersionId",
                table: "Agencies",
                column: "FeedVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Agencies_OperatorId",
                table: "Agencies",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_FeedId",
                table: "Alerts",
                column: "FeedId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarDates_FeedVersionId",
                table: "CalendarDates",
                column: "FeedVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Calendars_FeedVersionId",
                table: "Calendars",
                column: "FeedVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalRoutes_OnestopId",
                table: "CanonicalRoutes",
                column: "OnestopId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalRoutes_OperatorId",
                table: "CanonicalRoutes",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalStationOperators_OperatorId",
                table: "CanonicalStationOperators",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalStations_CityId",
                table: "CanonicalStations",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalStations_CountryId",
                table: "CanonicalStations",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalStations_OnestopId",
                table: "CanonicalStations",
                column: "OnestopId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalStations_ParentStationId",
                table: "CanonicalStations",
                column: "ParentStationId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalStations_PlaceId",
                table: "CanonicalStations",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Cities_CountryId",
                table: "Cities",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_IsoCode",
                table: "Countries",
                column: "IsoCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Feeds_OperatorId",
                table: "Feeds",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedVersions_FeedId_IsActive",
                table: "FeedVersions",
                columns: new[] { "FeedId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedVersions_Sha1",
                table: "FeedVersions",
                column: "Sha1");

            migrationBuilder.CreateIndex(
                name: "IX_MobilityProviders_OperatorId",
                table: "MobilityProviders",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilityStations_CityId",
                table: "MobilityStations",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilityStations_CountryId",
                table: "MobilityStations",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilityStations_MobilityProviderId",
                table: "MobilityStations",
                column: "MobilityProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Operators_CountryId",
                table: "Operators",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Operators_OnestopId",
                table: "Operators",
                column: "OnestopId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawStops_CanonicalStationId",
                table: "RawStops",
                column: "CanonicalStationId");

            migrationBuilder.CreateIndex(
                name: "IX_RawStops_FeedVersionId_RawStopId",
                table: "RawStops",
                columns: new[] { "FeedVersionId", "RawStopId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationCandidates_FeedId",
                table: "ReconciliationCandidates",
                column: "FeedId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationCandidates_RawStopId",
                table: "ReconciliationCandidates",
                column: "RawStopId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationCandidates_SuggestedCanonicalStationId",
                table: "ReconciliationCandidates",
                column: "SuggestedCanonicalStationId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_CountryId",
                table: "Regions",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Shapes_FeedVersionId",
                table: "Shapes",
                column: "FeedVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_StopTimes_CanonicalStationId",
                table: "StopTimes",
                column: "CanonicalStationId");

            migrationBuilder.CreateIndex(
                name: "IX_StopTimes_RawStopEntityId",
                table: "StopTimes",
                column: "RawStopEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_StopTimes_TripId",
                table: "StopTimes",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_CanonicalRouteId",
                table: "Trips",
                column: "CanonicalRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_FeedVersionId_TripId",
                table: "Trips",
                columns: new[] { "FeedVersionId", "TripId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Agencies");

            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "CalendarDates");

            migrationBuilder.DropTable(
                name: "Calendars");

            migrationBuilder.DropTable(
                name: "CanonicalStationOperators");

            migrationBuilder.DropTable(
                name: "MobilityStations");

            migrationBuilder.DropTable(
                name: "ReconciliationCandidates");

            migrationBuilder.DropTable(
                name: "Regions");

            migrationBuilder.DropTable(
                name: "Shapes");

            migrationBuilder.DropTable(
                name: "StopTimes");

            migrationBuilder.DropTable(
                name: "MobilityProviders");

            migrationBuilder.DropTable(
                name: "RawStops");

            migrationBuilder.DropTable(
                name: "Trips");

            migrationBuilder.DropTable(
                name: "CanonicalStations");

            migrationBuilder.DropTable(
                name: "CanonicalRoutes");

            migrationBuilder.DropTable(
                name: "FeedVersions");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropTable(
                name: "Places");

            migrationBuilder.DropTable(
                name: "Feeds");

            migrationBuilder.DropTable(
                name: "Operators");

            migrationBuilder.DropTable(
                name: "Countries");
        }
    }
}
