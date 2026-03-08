using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddRealtimeFormatColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RealtimeAdapterConfig",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RealtimeAuthConfig",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RealtimeAuthType",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RealtimeFeedFormat",
                table: "TransitOperators",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RealtimeAdapterConfig",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "RealtimeAuthConfig",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "RealtimeAuthType",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "RealtimeFeedFormat",
                table: "TransitOperators");
        }
    }
}
