using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketUploadsAndEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DestinationName",
                table: "ImportedTickets",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginName",
                table: "ImportedTickets",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TicketUploads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BlobKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FileType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketUploads_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketUploads_BlobKey",
                table: "TicketUploads",
                column: "BlobKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketUploads_UserId_ConsumedAt",
                table: "TicketUploads",
                columns: new[] { "UserId", "ConsumedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketUploads");

            migrationBuilder.DropColumn(
                name: "DestinationName",
                table: "ImportedTickets");

            migrationBuilder.DropColumn(
                name: "OriginName",
                table: "ImportedTickets");
        }
    }
}
