using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyTransitOperatorFeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GtfsRtAlertsUrl",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "GtfsRtTripUpdatesUrl",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "GtfsRtVehiclePositionsUrl",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "OtpFeedId",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "OtpInstanceKey",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "RealtimeFallbackMode",
                table: "TransitOperators");

            migrationBuilder.UpdateData(
                table: "TransitOperators",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "GtfsRealtimeFeedUrl", "RealtimeFeedFormat" },
                values: new object[] { "http://127.0.0.1:5000/hzpp-rt", "GTFS_RT_PROTO" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GtfsRtAlertsUrl",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GtfsRtTripUpdatesUrl",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GtfsRtVehiclePositionsUrl",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OtpFeedId",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OtpInstanceKey",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RealtimeFallbackMode",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "TransitOperators",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "GtfsRtAlertsUrl", "GtfsRtTripUpdatesUrl", "GtfsRtVehiclePositionsUrl", "OtpFeedId", "OtpInstanceKey", "RealtimeFallbackMode" },
                values: new object[] { null, "https://zet.hr/gtfs-rt-protobuf", null, "zet", "eu", "None" });

            migrationBuilder.UpdateData(
                table: "TransitOperators",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "GtfsRealtimeFeedUrl", "GtfsRtAlertsUrl", "GtfsRtTripUpdatesUrl", "GtfsRtVehiclePositionsUrl", "OtpFeedId", "OtpInstanceKey", "RealtimeFallbackMode", "RealtimeFeedFormat" },
                values: new object[] { null, null, null, null, "hzpp", "eu", "HZPP_Scraper", "NONE" });

            migrationBuilder.UpdateData(
                table: "TransitOperators",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "GtfsRtAlertsUrl", "GtfsRtTripUpdatesUrl", "GtfsRtVehiclePositionsUrl", "OtpFeedId", "OtpInstanceKey", "RealtimeFallbackMode" },
                values: new object[] { null, null, null, "obb", "eu", "None" });
        }
    }
}
