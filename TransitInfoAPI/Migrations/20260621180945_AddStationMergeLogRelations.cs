using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddStationMergeLogRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MovedRawStopIds",
                table: "StationMergeLogs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StationMergeLogs_CanonicalStations_SourceStationId",
                table: "StationMergeLogs",
                column: "SourceStationId",
                principalTable: "CanonicalStations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StationMergeLogs_CanonicalStations_TargetStationId",
                table: "StationMergeLogs",
                column: "TargetStationId",
                principalTable: "CanonicalStations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StationMergeLogs_CanonicalStations_SourceStationId",
                table: "StationMergeLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_StationMergeLogs_CanonicalStations_TargetStationId",
                table: "StationMergeLogs");

            migrationBuilder.DropColumn(
                name: "MovedRawStopIds",
                table: "StationMergeLogs");
        }
    }
}
