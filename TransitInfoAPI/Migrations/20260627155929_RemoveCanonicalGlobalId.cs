using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCanonicalGlobalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GlobalId",
                table: "CanonicalStations");

            migrationBuilder.DropColumn(
                name: "GlobalId",
                table: "CanonicalRoutes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GlobalId",
                table: "CanonicalStations",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GlobalId",
                table: "CanonicalRoutes",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
