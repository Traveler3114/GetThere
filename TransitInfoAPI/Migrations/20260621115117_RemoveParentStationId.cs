using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveParentStationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CanonicalStations_CanonicalStations_ParentStationId",
                table: "CanonicalStations");

            migrationBuilder.DropIndex(
                name: "IX_CanonicalStations_ParentStationId",
                table: "CanonicalStations");

            migrationBuilder.DropColumn(
                name: "ParentStationId",
                table: "CanonicalStations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentStationId",
                table: "CanonicalStations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalStations_ParentStationId",
                table: "CanonicalStations",
                column: "ParentStationId");

            migrationBuilder.AddForeignKey(
                name: "FK_CanonicalStations_CanonicalStations_ParentStationId",
                table: "CanonicalStations",
                column: "ParentStationId",
                principalTable: "CanonicalStations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
