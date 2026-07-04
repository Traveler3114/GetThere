using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddDistinctByIsStaticMappingKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DistinctBy",
                table: "CustomFeedTableConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsStatic",
                table: "CustomFeedTableConfigs",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DistinctBy",
                table: "CustomFeedTableConfigs");

            migrationBuilder.DropColumn(
                name: "IsStatic",
                table: "CustomFeedTableConfigs");
        }
    }
}
