using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class ExtraTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Payments");

            migrationBuilder.AddColumn<int>(
                name: "TransitOperatorId",
                table: "Tickets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentProviderId",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiBaseUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApiKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WebhookSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransitOperators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Region = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiBaseUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApiKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransitOperators", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TransitOperatorId",
                table: "Tickets",
                column: "TransitOperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentProviderId",
                table: "Payments",
                column: "PaymentProviderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_PaymentProviders_PaymentProviderId",
                table: "Payments",
                column: "PaymentProviderId",
                principalTable: "PaymentProviders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_TransitOperators_TransitOperatorId",
                table: "Tickets",
                column: "TransitOperatorId",
                principalTable: "TransitOperators",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_PaymentProviders_PaymentProviderId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_TransitOperators_TransitOperatorId",
                table: "Tickets");

            migrationBuilder.DropTable(
                name: "PaymentProviders");

            migrationBuilder.DropTable(
                name: "TransitOperators");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_TransitOperatorId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Payments_PaymentProviderId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "TransitOperatorId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "PaymentProviderId",
                table: "Payments");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
