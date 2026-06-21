using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class UpdateStationMergeLogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Detail",
                table: "StationMergeLogs");

            migrationBuilder.DropColumn(
                name: "OperatorsMerged",
                table: "StationMergeLogs");

            migrationBuilder.RenameColumn(
                name: "RawStopsMoved",
                table: "StationMergeLogs",
                newName: "RawStopsMovedCount");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "StationMergeLogs",
                newName: "MergedAt");

            migrationBuilder.AddColumn<string>(
                name: "SourceStationGlobalId",
                table: "StationMergeLogs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceStationGlobalId",
                table: "StationMergeLogs");

            migrationBuilder.RenameColumn(
                name: "RawStopsMovedCount",
                table: "StationMergeLogs",
                newName: "RawStopsMoved");

            migrationBuilder.RenameColumn(
                name: "MergedAt",
                table: "StationMergeLogs",
                newName: "CreatedAt");

            migrationBuilder.AddColumn<string>(
                name: "Detail",
                table: "StationMergeLogs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OperatorsMerged",
                table: "StationMergeLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
