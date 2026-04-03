using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddMobilityProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MobilityProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FeedFormat = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApiBaseUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApiKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdapterConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilityProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MobilityProviderCity",
                columns: table => new
                {
                    CitiesId = table.Column<int>(type: "int", nullable: false),
                    MobilityProvidersId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilityProviderCity", x => new { x.CitiesId, x.MobilityProvidersId });
                    table.ForeignKey(
                        name: "FK_MobilityProviderCity_Cities_CitiesId",
                        column: x => x.CitiesId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MobilityProviderCity_MobilityProviders_MobilityProvidersId",
                        column: x => x.MobilityProvidersId,
                        principalTable: "MobilityProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MobilityProviderCountry",
                columns: table => new
                {
                    CountriesId = table.Column<int>(type: "int", nullable: false),
                    MobilityProvidersId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilityProviderCountry", x => new { x.CountriesId, x.MobilityProvidersId });
                    table.ForeignKey(
                        name: "FK_MobilityProviderCountry_Countries_CountriesId",
                        column: x => x.CountriesId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MobilityProviderCountry_MobilityProviders_MobilityProvidersId",
                        column: x => x.MobilityProvidersId,
                        principalTable: "MobilityProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "MobilityProviders",
                columns: new[] { "Id", "AdapterConfig", "ApiBaseUrl", "ApiKey", "CreatedAt", "FeedFormat", "LogoUrl", "Name", "Type" },
                values: new object[] { 1, "{\"cityUid\": 483}", "https://nextbike.net/maps/nextbike-live.json", null, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "NEXTBIKE_API", null, "Bajs / Nextbike Zagreb", "BIKE_STATION" });

            migrationBuilder.InsertData(
                table: "MobilityProviderCity",
                columns: new[] { "CitiesId", "MobilityProvidersId" },
                values: new object[] { 1, 1 });

            migrationBuilder.InsertData(
                table: "MobilityProviderCountry",
                columns: new[] { "CountriesId", "MobilityProvidersId" },
                values: new object[] { 1, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_MobilityProviderCity_MobilityProvidersId",
                table: "MobilityProviderCity",
                column: "MobilityProvidersId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilityProviderCountry_MobilityProvidersId",
                table: "MobilityProviderCountry",
                column: "MobilityProvidersId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MobilityProviderCity");

            migrationBuilder.DropTable(
                name: "MobilityProviderCountry");

            migrationBuilder.DropTable(
                name: "MobilityProviders");
        }
    }
}
