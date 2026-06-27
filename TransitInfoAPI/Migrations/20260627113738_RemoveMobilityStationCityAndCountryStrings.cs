using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMobilityStationCityAndCountryStrings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MobilityStations_Cities_CityId",
                table: "MobilityStations");

            migrationBuilder.DropIndex(
                name: "IX_MobilityStations_CityId",
                table: "MobilityStations");

            migrationBuilder.DropColumn(
                name: "CityId",
                table: "MobilityStations");

            migrationBuilder.DropColumn(
                name: "CityName",
                table: "MobilityStations");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "MobilityStations");

            migrationBuilder.DropColumn(
                name: "CountryName",
                table: "MobilityStations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CityId",
                table: "MobilityStations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CityName",
                table: "MobilityStations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "MobilityStations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryName",
                table: "MobilityStations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MobilityStations_CityId",
                table: "MobilityStations",
                column: "CityId");

            migrationBuilder.AddForeignKey(
                name: "FK_MobilityStations_Cities_CityId",
                table: "MobilityStations",
                column: "CityId",
                principalTable: "Cities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
