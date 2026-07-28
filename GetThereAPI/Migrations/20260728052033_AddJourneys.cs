using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddJourneys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "JourneyId",
                table: "Tickets",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Journeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Journeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Journeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_JourneyId",
                table: "Tickets",
                column: "JourneyId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportedTickets_JourneyId",
                table: "ImportedTickets",
                column: "JourneyId");

            migrationBuilder.CreateIndex(
                name: "IX_Journeys_UserId_StartsAt",
                table: "Journeys",
                columns: new[] { "UserId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Journeys_UserId_Status",
                table: "Journeys",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_ImportedTickets_Journeys_JourneyId",
                table: "ImportedTickets",
                column: "JourneyId",
                principalTable: "Journeys",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Journeys_JourneyId",
                table: "Tickets",
                column: "JourneyId",
                principalTable: "Journeys",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImportedTickets_Journeys_JourneyId",
                table: "ImportedTickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Journeys_JourneyId",
                table: "Tickets");

            migrationBuilder.DropTable(
                name: "Journeys");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_JourneyId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_ImportedTickets_JourneyId",
                table: "ImportedTickets");

            migrationBuilder.DropColumn(
                name: "JourneyId",
                table: "Tickets");
        }
    }
}
