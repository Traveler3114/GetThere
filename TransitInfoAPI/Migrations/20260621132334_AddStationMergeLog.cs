using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddStationMergeLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StationMergeLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceStationId = table.Column<int>(type: "int", nullable: false),
                    TargetStationId = table.Column<int>(type: "int", nullable: false),
                    RawStopsMoved = table.Column<int>(type: "int", nullable: false),
                    OperatorsMerged = table.Column<int>(type: "int", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationMergeLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StationMergeLogs_SourceStationId",
                table: "StationMergeLogs",
                column: "SourceStationId");

            migrationBuilder.CreateIndex(
                name: "IX_StationMergeLogs_TargetStationId",
                table: "StationMergeLogs",
                column: "TargetStationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StationMergeLogs");
        }
    }
}
