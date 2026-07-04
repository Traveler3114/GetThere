using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceQueriesWithTableConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Queries",
                table: "CustomFeeds");

            migrationBuilder.CreateTable(
                name: "CustomFeedTableConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomFeedId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HttpMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DataPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetTable = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaginationConfig = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFeedTableConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomFeedTableConfigs_CustomFeeds_CustomFeedId",
                        column: x => x.CustomFeedId,
                        principalTable: "CustomFeeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomFeedTableFieldMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomFeedTableConfigId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    SourceExpression = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetField = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MappingKind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFeedTableFieldMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomFeedTableFieldMappings_CustomFeedTableConfigs_CustomFeedTableConfigId",
                        column: x => x.CustomFeedTableConfigId,
                        principalTable: "CustomFeedTableConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomFeedTableConfigs_CustomFeedId",
                table: "CustomFeedTableConfigs",
                column: "CustomFeedId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomFeedTableFieldMappings_CustomFeedTableConfigId",
                table: "CustomFeedTableFieldMappings",
                column: "CustomFeedTableConfigId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomFeedTableFieldMappings");

            migrationBuilder.DropTable(
                name: "CustomFeedTableConfigs");

            migrationBuilder.AddColumn<string>(
                name: "Queries",
                table: "CustomFeeds",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
