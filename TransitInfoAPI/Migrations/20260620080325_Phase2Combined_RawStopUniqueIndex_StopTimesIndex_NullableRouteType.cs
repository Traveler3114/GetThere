using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class Phase2Combined_RawStopUniqueIndex_StopTimesIndex_NullableRouteType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RawStops_FeedVersionId_RawStopId",
                table: "RawStops");

            migrationBuilder.AlterColumn<string>(
                name: "RouteType",
                table: "RawStops",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_RawStops_FeedVersionId_RawStopId",
                table: "RawStops",
                columns: new[] { "FeedVersionId", "RawStopId" },
                unique: true);

            migrationBuilder.Sql(
                "CREATE INDEX IX_StopTimes_RawStopId ON StopTimes (RawStopId) INCLUDE (CanonicalStationId, RawStopEntityId)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IX_StopTimes_RawStopId ON StopTimes");

            migrationBuilder.DropIndex(
                name: "IX_RawStops_FeedVersionId_RawStopId",
                table: "RawStops");

            migrationBuilder.AlterColumn<string>(
                name: "RouteType",
                table: "RawStops",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawStops_FeedVersionId_RawStopId",
                table: "RawStops",
                columns: new[] { "FeedVersionId", "RawStopId" });
        }
    }
}
