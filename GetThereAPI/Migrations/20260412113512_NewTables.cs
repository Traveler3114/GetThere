using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class NewTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLogin = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MobilityProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FeedFormat = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApiBaseUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApiKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdapterConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilityProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApiBaseUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApiKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WebhookSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransportTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GtfsRouteType = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IconFile = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Balance = table.Column<decimal>(type: "decimal(16,2)", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wallets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MobilityProviderCountry",
                columns: table => new
                {
                    CountriesId = table.Column<int>(type: "int", nullable: false),
                    MobilityProvidersId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilityProviderCountry", x => new { x.CountriesId, x.MobilityProvidersId });
                    table.ForeignKey(
                        name: "FK_MobilityProviderCountry_Countries_CountriesId",
                        column: x => x.CountriesId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MobilityProviderCountry_MobilityProviders_MobilityProvidersId",
                        column: x => x.MobilityProvidersId,
                        principalTable: "MobilityProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Amount = table.Column<decimal>(type: "decimal(16,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WalletId = table.Column<int>(type: "int", nullable: false),
                    PaymentProviderId = table.Column<int>(type: "int", nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_PaymentProviders_PaymentProviderId",
                        column: x => x.PaymentProviderId,
                        principalTable: "PaymentProviders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Payments_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MobilityProviderCity",
                columns: table => new
                {
                    CitiesId = table.Column<int>(type: "int", nullable: false),
                    MobilityProvidersId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilityProviderCity", x => new { x.CitiesId, x.MobilityProvidersId });
                    table.ForeignKey(
                        name: "FK_MobilityProviderCity_Cities_CitiesId",
                        column: x => x.CitiesId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MobilityProviderCity_MobilityProviders_MobilityProvidersId",
                        column: x => x.MobilityProvidersId,
                        principalTable: "MobilityProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransitOperators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TicketApiBaseUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TicketApiKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GtfsFeedUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StaticFeedFormat = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GtfsRealtimeFeedUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RealtimeFeedFormat = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RealtimeAuthType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RealtimeAuthConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RealtimeAdapterConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    CityId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransitOperators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransitOperators_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TransitOperators_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PricePaid = table.Column<decimal>(type: "decimal(16,2)", nullable: false),
                    Format = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayInstructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TicketDefinitionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TransitOperatorId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tickets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Tickets_TransitOperators_TransitOperatorId",
                        column: x => x.TransitOperatorId,
                        principalTable: "TransitOperators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransitOperatorTransportType",
                columns: table => new
                {
                    OperatorsId = table.Column<int>(type: "int", nullable: false),
                    TransportTypesId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransitOperatorTransportType", x => new { x.OperatorsId, x.TransportTypesId });
                    table.ForeignKey(
                        name: "FK_TransitOperatorTransportType_TransitOperators_OperatorsId",
                        column: x => x.OperatorsId,
                        principalTable: "TransitOperators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransitOperatorTransportType_TransportTypes_TransportTypesId",
                        column: x => x.TransportTypesId,
                        principalTable: "TransportTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(16,2)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WalletId = table.Column<int>(type: "int", nullable: false),
                    TicketId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Countries",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Croatia" },
                    { 2, "Slovenia" },
                    { 3, "Austria" },
                    { 4, "Germany" },
                    { 5, "France" },
                    { 6, "Italy" },
                    { 7, "Poland" },
                    { 8, "Czechia" },
                    { 9, "Hungary" },
                    { 10, "Switzerland" },
                    { 11, "Slovakia" },
                    { 12, "Spain" }
                });

            migrationBuilder.InsertData(
                table: "MobilityProviders",
                columns: new[] { "Id", "AdapterConfig", "ApiBaseUrl", "ApiKey", "CreatedAt", "FeedFormat", "LogoUrl", "Name", "Type" },
                values: new object[] { 1, null, "https://nextbike.net/maps/nextbike-live.json", null, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "NEXTBIKE_API", null, "Bajs / Nextbike", "BIKE_STATION" });

            migrationBuilder.InsertData(
                table: "PaymentProviders",
                columns: new[] { "Id", "ApiBaseUrl", "ApiKey", "CreatedAt", "IsActive", "Name", "WebhookSecret" },
                values: new object[,]
                {
                    { 1, "https://mockpay.example.com", "MOCK_API_KEY", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "MockPay", null },
                    { 2, "https://testpay.example.com", "TEST_API_KEY", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "TestPay", "SECRET123" }
                });

            migrationBuilder.InsertData(
                table: "TransportTypes",
                columns: new[] { "Id", "Color", "GtfsRouteType", "IconFile", "Name" },
                values: new object[,]
                {
                    { 1, "#1264AB", 0, "tram.png", "Tram" },
                    { 2, "#126400", 3, "bus.png", "Bus" },
                    { 3, "#FF6B00", 2, "train.png", "Train" }
                });

            migrationBuilder.InsertData(
                table: "Cities",
                columns: new[] { "Id", "CountryId", "Name" },
                values: new object[,]
                {
                    { 1, 1, "Zagreb" },
                    { 2, 2, "Ljubljana" },
                    { 3, 3, "Vienna" },
                    { 4, 4, "Berlin" },
                    { 5, 5, "Paris" },
                    { 6, 6, "Rome" },
                    { 7, 7, "Warsaw" },
                    { 8, 8, "Prague" },
                    { 9, 9, "Budapest" },
                    { 10, 10, "Zurich" },
                    { 11, 11, "Bratislava" },
                    { 12, 12, "Madrid" }
                });

            migrationBuilder.InsertData(
                table: "TransitOperators",
                columns: new[] { "Id", "CityId", "CountryId", "CreatedAt", "GtfsFeedUrl", "GtfsRealtimeFeedUrl", "LogoUrl", "Name", "RealtimeAdapterConfig", "RealtimeAuthConfig", "RealtimeAuthType", "RealtimeFeedFormat", "StaticFeedFormat", "TicketApiBaseUrl", "TicketApiKey" },
                values: new object[,]
                {
                    { 2, null, 1, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://www.hzpp.hr/GTFS_files.zip", null, null, "HZPP", null, null, "NONE", "NONE", "GTFS", "", "" },
                    { 3, null, 3, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://static.web.oebb.at/open-data/soll-fahrplan-gtfs/GTFS_Fahrplan_2026.zip", null, null, "ÖBB (Austrian Federal Railways)", null, null, "NONE", "NONE", "GTFS", "", "" },
                    { 1, 1, 1, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://zet.hr/gtfs-scheduled/latest", "https://zet.hr/gtfs-rt-protobuf", null, "ZET", null, null, "NONE", "GTFS_RT_PROTO", "GTFS", "", "" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Cities_CountryId",
                table: "Cities",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilityProviderCity_MobilityProvidersId",
                table: "MobilityProviderCity",
                column: "MobilityProvidersId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilityProviderCountry_MobilityProvidersId",
                table: "MobilityProviderCountry",
                column: "MobilityProvidersId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentProviderId",
                table: "Payments",
                column: "PaymentProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_WalletId",
                table: "Payments",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TransitOperatorId",
                table: "Tickets",
                column: "TransitOperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_UserId",
                table: "Tickets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TransitOperators_CityId",
                table: "TransitOperators",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_TransitOperators_CountryId",
                table: "TransitOperators",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_TransitOperatorTransportType_TransportTypesId",
                table: "TransitOperatorTransportType",
                column: "TransportTypesId");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_UserId",
                table: "Wallets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_TicketId",
                table: "WalletTransactions",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId",
                table: "WalletTransactions",
                column: "WalletId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "MobilityProviderCity");

            migrationBuilder.DropTable(
                name: "MobilityProviderCountry");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "TransitOperatorTransportType");

            migrationBuilder.DropTable(
                name: "WalletTransactions");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "MobilityProviders");

            migrationBuilder.DropTable(
                name: "PaymentProviders");

            migrationBuilder.DropTable(
                name: "TransportTypes");

            migrationBuilder.DropTable(
                name: "Tickets");

            migrationBuilder.DropTable(
                name: "Wallets");

            migrationBuilder.DropTable(
                name: "TransitOperators");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropTable(
                name: "Countries");
        }
    }
}
