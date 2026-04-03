using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class NewMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "TransportTypes",
                keyColumn: "Id",
                keyValue: 3,
                column: "Color",
                value: "#FF6B00");

            migrationBuilder.InsertData(
                table: "TransportTypes",
                columns: new[] { "Id", "Color", "GtfsRouteType", "IconFile", "Name" },
                values: new object[] { 4, "#6a1b9a", 715, "bike.png", "City Bike" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "TransportTypes",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.UpdateData(
                table: "TransportTypes",
                keyColumn: "Id",
                keyValue: 3,
                column: "Color",
                value: "#6a1b9a");
        }
    }
}
