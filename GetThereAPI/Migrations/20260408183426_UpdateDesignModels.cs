using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDesignModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "TransitOperators",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.InsertData(
                table: "Cities",
                columns: new[] { "Id", "CountryId", "Name" },
                values: new object[] { 3, 3, "Vienna" });

            migrationBuilder.InsertData(
                table: "Countries",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
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

            migrationBuilder.UpdateData(
                table: "TransitOperators",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CityId", "CountryId", "GtfsFeedUrl", "Name" },
                values: new object[] { null, 3, "https://static.web.oebb.at/open-data/soll-fahrplan-gtfs/GTFS_Fahrplan_2026.zip", "ÖBB (Austrian Federal Railways)" });

            migrationBuilder.InsertData(
                table: "Cities",
                columns: new[] { "Id", "CountryId", "Name" },
                values: new object[,]
                {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Cities",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Cities",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Cities",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Cities",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Cities",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Cities",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Cities",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Cities",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "Cities",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "Cities",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "Countries",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Countries",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Countries",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Countries",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Countries",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Countries",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Countries",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "Countries",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "Countries",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.UpdateData(
                table: "TransitOperators",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CityId", "CountryId", "GtfsFeedUrl", "Name" },
                values: new object[] { 2, 2, "https://data.lpp.si/api/gtfs/feed.zip", "LPP" });

            migrationBuilder.InsertData(
                table: "TransitOperators",
                columns: new[] { "Id", "CityId", "CountryId", "CreatedAt", "GtfsFeedUrl", "GtfsRealtimeFeedUrl", "LogoUrl", "Name", "RealtimeAdapterConfig", "RealtimeAuthConfig", "RealtimeAuthType", "RealtimeFeedFormat", "StaticFeedFormat", "TicketApiBaseUrl", "TicketApiKey" },
                values: new object[] { 4, null, 3, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://data.oebb.at/oebb-gtfs/full.zip", null, null, "OBB", null, null, "NONE", "NONE", "GTFS", "", "" });
        }
    }
}
