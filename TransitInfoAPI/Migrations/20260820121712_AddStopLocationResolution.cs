using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddStopLocationResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "RegionCentroidLat",
                table: "Operators",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RegionCentroidLon",
                table: "Operators",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegionName",
                table: "Operators",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RegionRadiusKm",
                table: "Operators",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provenance",
                table: "Feeds",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Official");

            migrationBuilder.CreateTable(
                name: "StopGazetteerEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Lat = table.Column<double>(type: "float", nullable: false),
                    Lon = table.Column<double>(type: "float", nullable: false),
                    Operator = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Network = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StopGazetteerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StopLocationCacheEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OperatorId = table.Column<int>(type: "int", nullable: false),
                    Lat = table.Column<double>(type: "float", nullable: false),
                    Lon = table.Column<double>(type: "float", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StopLocationCacheEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StopGazetteerEntries_Region_NormalizedName",
                table: "StopGazetteerEntries",
                columns: new[] { "Region", "NormalizedName" });

            migrationBuilder.CreateIndex(
                name: "IX_StopLocationCacheEntries_NormalizedName_OperatorId",
                table: "StopLocationCacheEntries",
                columns: new[] { "NormalizedName", "OperatorId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StopGazetteerEntries");

            migrationBuilder.DropTable(
                name: "StopLocationCacheEntries");

            migrationBuilder.DropColumn(
                name: "RegionCentroidLat",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "RegionCentroidLon",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "RegionName",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "RegionRadiusKm",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "Provenance",
                table: "Feeds");
        }
    }
}
