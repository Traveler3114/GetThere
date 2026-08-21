using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomSourceMobility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ProducesMobility",
                table: "CustomSources",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProducesMobility",
                table: "CustomSources");
        }
    }
}
