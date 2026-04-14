using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyTransitOperatorFeedColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RealtimeAdapterConfig",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "RealtimeAuthConfig",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "RealtimeAuthType",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "RealtimeFeedFormat",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "StaticFeedFormat",
                table: "TransitOperators");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RealtimeAdapterConfig",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RealtimeAuthConfig",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RealtimeAuthType",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RealtimeFeedFormat",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StaticFeedFormat",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "TransitOperators",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "RealtimeAdapterConfig", "RealtimeAuthConfig", "RealtimeAuthType", "RealtimeFeedFormat", "StaticFeedFormat" },
                values: new object[] { null, null, "NONE", "GTFS_RT_PROTO", "GTFS" });

            migrationBuilder.UpdateData(
                table: "TransitOperators",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "RealtimeAdapterConfig", "RealtimeAuthConfig", "RealtimeAuthType", "RealtimeFeedFormat", "StaticFeedFormat" },
                values: new object[] { null, null, "NONE", "GTFS_RT_PROTO", "GTFS" });

            migrationBuilder.UpdateData(
                table: "TransitOperators",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "RealtimeAdapterConfig", "RealtimeAuthConfig", "RealtimeAuthType", "RealtimeFeedFormat", "StaticFeedFormat" },
                values: new object[] { null, null, "NONE", "NONE", "GTFS" });
        }
    }
}
