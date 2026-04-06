using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class NewRow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "TransportTypes",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.InsertData(
                table: "Countries",
                columns: new[] { "Id", "Name" },
                values: new object[] { 3, "Austria" });

            migrationBuilder.InsertData(
                table: "TransitOperators",
                columns: new[] { "Id", "CityId", "CountryId", "CreatedAt", "GtfsFeedUrl", "GtfsRealtimeFeedUrl", "LogoUrl", "Name", "RealtimeAdapterConfig", "RealtimeAuthConfig", "RealtimeAuthType", "RealtimeFeedFormat", "StaticFeedFormat", "TicketApiBaseUrl", "TicketApiKey" },
                values: new object[] { 4, null, 3, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://data.oebb.at/oebb-gtfs/full.zip", null, null, "OBB", null, null, "NONE", "NONE", "GTFS", "", "" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "TransitOperators",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Countries",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.InsertData(
                table: "TransportTypes",
                columns: new[] { "Id", "Color", "GtfsRouteType", "IconFile", "Name" },
                values: new object[] { 4, "#6a1b9a", 715, "bike.png", "City Bike" });
        }
    }
}
