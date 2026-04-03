using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveNextbikeCityUidFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "MobilityProviders",
                keyColumn: "Id",
                keyValue: 1,
                column: "AdapterConfig",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "MobilityProviders",
                keyColumn: "Id",
                keyValue: 1,
                column: "AdapterConfig",
                value: "{\"cityUid\": 483}");
        }
    }
}
