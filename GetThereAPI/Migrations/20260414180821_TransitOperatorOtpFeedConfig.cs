using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class TransitOperatorOtpFeedConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
                defaultValue: "eu");

            migrationBuilder.AddColumn<string>(
                name: "RealtimeFallbackMode",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "None");

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
                columns: new[] { "GtfsRtAlertsUrl", "GtfsRtTripUpdatesUrl", "GtfsRtVehiclePositionsUrl", "OtpFeedId", "OtpInstanceKey", "RealtimeFallbackMode" },
                values: new object[] { null, null, null, "hzpp", "eu", "HZPP_Scraper" });

            migrationBuilder.UpdateData(
                table: "TransitOperators",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "GtfsRtAlertsUrl", "GtfsRtTripUpdatesUrl", "GtfsRtVehiclePositionsUrl", "OtpFeedId", "OtpInstanceKey", "RealtimeFallbackMode" },
                values: new object[] { null, null, null, "obb", "eu", "None" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
        }
    }
}
