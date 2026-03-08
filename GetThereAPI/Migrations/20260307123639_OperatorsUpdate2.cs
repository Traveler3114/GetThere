using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class OperatorsUpdate2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "IsRealtimeEnabled",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "IsScheduleEnabled",
                table: "TransitOperators");

            migrationBuilder.DropColumn(
                name: "IsTicketingEnabled",
                table: "TransitOperators");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "TransitOperators",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRealtimeEnabled",
                table: "TransitOperators",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsScheduleEnabled",
                table: "TransitOperators",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTicketingEnabled",
                table: "TransitOperators",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
