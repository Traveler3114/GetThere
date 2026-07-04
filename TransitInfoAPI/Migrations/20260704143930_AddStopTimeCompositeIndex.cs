using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddStopTimeCompositeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StopTimes_CanonicalStationId",
                table: "StopTimes");

            migrationBuilder.CreateIndex(
                name: "IX_StopTimes_CanonicalStationId_DepartureTime",
                table: "StopTimes",
                columns: new[] { "CanonicalStationId", "DepartureTime" })
                .Annotation("SqlServer:Include", new[] { "TripId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StopTimes_CanonicalStationId_DepartureTime",
                table: "StopTimes");

            migrationBuilder.CreateIndex(
                name: "IX_StopTimes_CanonicalStationId",
                table: "StopTimes",
                column: "CanonicalStationId");
        }
    }
}
