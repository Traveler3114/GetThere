using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMobilityProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomFeeds_MobilityProviders_MobilityProviderId",
                table: "CustomFeeds");

            migrationBuilder.DropForeignKey(
                name: "FK_MobilityStations_MobilityProviders_MobilityProviderId",
                table: "MobilityStations");

            migrationBuilder.DropTable(
                name: "MobilityProviders");

            migrationBuilder.DropIndex(
                name: "IX_CustomFeeds_MobilityProviderId",
                table: "CustomFeeds");

            migrationBuilder.DropColumn(
                name: "MobilityProviderId",
                table: "CustomFeeds");

            migrationBuilder.RenameColumn(
                name: "MobilityProviderId",
                table: "MobilityStations",
                newName: "OperatorId");

            migrationBuilder.RenameIndex(
                name: "IX_MobilityStations_MobilityProviderId",
                table: "MobilityStations",
                newName: "IX_MobilityStations_OperatorId");

            migrationBuilder.AddForeignKey(
                name: "FK_MobilityStations_Operators_OperatorId",
                table: "MobilityStations",
                column: "OperatorId",
                principalTable: "Operators",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MobilityStations_Operators_OperatorId",
                table: "MobilityStations");

            migrationBuilder.RenameColumn(
                name: "OperatorId",
                table: "MobilityStations",
                newName: "MobilityProviderId");

            migrationBuilder.RenameIndex(
                name: "IX_MobilityStations_OperatorId",
                table: "MobilityStations",
                newName: "IX_MobilityStations_MobilityProviderId");

            migrationBuilder.AddColumn<int>(
                name: "MobilityProviderId",
                table: "CustomFeeds",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MobilityProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperatorId = table.Column<int>(type: "int", nullable: false),
                    ApiKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConverterConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FeedFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilityProviders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MobilityProviders_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomFeeds_MobilityProviderId",
                table: "CustomFeeds",
                column: "MobilityProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilityProviders_OperatorId",
                table: "MobilityProviders",
                column: "OperatorId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomFeeds_MobilityProviders_MobilityProviderId",
                table: "CustomFeeds",
                column: "MobilityProviderId",
                principalTable: "MobilityProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MobilityStations_MobilityProviders_MobilityProviderId",
                table: "MobilityStations",
                column: "MobilityProviderId",
                principalTable: "MobilityProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
