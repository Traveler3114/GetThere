using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddJourneyReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JourneyReservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JourneyId = table.Column<int>(type: "int", nullable: false),
                    OperatorGlobalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OperatorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    WalletHoldReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JourneyReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JourneyReservations_Journeys_JourneyId",
                        column: x => x.JourneyId,
                        principalTable: "Journeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JourneyReservations_JourneyId",
                table: "JourneyReservations",
                column: "JourneyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JourneyReservations");
        }
    }
}
