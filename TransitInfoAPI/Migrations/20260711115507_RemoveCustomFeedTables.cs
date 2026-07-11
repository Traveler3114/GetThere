using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCustomFeedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Feeds_CustomFeeds_CustomFeedId",
                table: "Feeds");

            migrationBuilder.DropTable(
                name: "CustomFeedFieldMappings");

            migrationBuilder.DropTable(
                name: "CustomFeedRuns");

            migrationBuilder.DropTable(
                name: "CustomFeedTableFieldMappings");

            migrationBuilder.DropTable(
                name: "CustomFeedTableConfigs");

            migrationBuilder.DropTable(
                name: "CustomFeeds");

            migrationBuilder.DropIndex(
                name: "IX_Feeds_CustomFeedId",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "CustomFeedId",
                table: "Feeds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomFeedId",
                table: "Feeds",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomFeeds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperatorId = table.Column<int>(type: "int", nullable: false),
                    AuthConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BaseUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HttpMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsScheduleCapable = table.Column<bool>(type: "bit", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutputFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PaginationConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefreshIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    ResponseFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetTable = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFeeds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomFeeds_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomFeedFieldMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomFeedId = table.Column<int>(type: "int", nullable: false),
                    MappingKind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    SourceExpression = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetField = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFeedFieldMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomFeedFieldMappings_CustomFeeds_CustomFeedId",
                        column: x => x.CustomFeedId,
                        principalTable: "CustomFeeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomFeedRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomFeedId = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LogText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordsProduced = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFeedRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomFeedRuns_CustomFeeds_CustomFeedId",
                        column: x => x.CustomFeedId,
                        principalTable: "CustomFeeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomFeedTableConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomFeedId = table.Column<int>(type: "int", nullable: false),
                    DataPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DistinctBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HttpMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsStatic = table.Column<bool>(type: "bit", nullable: false),
                    PaginationConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TargetTable = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                    MappingKind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    SourceExpression = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetField = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                name: "IX_Feeds_CustomFeedId",
                table: "Feeds",
                column: "CustomFeedId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomFeedFieldMappings_CustomFeedId",
                table: "CustomFeedFieldMappings",
                column: "CustomFeedId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomFeedRuns_CustomFeedId",
                table: "CustomFeedRuns",
                column: "CustomFeedId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomFeeds_OperatorId",
                table: "CustomFeeds",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomFeedTableConfigs_CustomFeedId",
                table: "CustomFeedTableConfigs",
                column: "CustomFeedId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomFeedTableFieldMappings_CustomFeedTableConfigId",
                table: "CustomFeedTableFieldMappings",
                column: "CustomFeedTableConfigId");

            migrationBuilder.AddForeignKey(
                name: "FK_Feeds_CustomFeeds_CustomFeedId",
                table: "Feeds",
                column: "CustomFeedId",
                principalTable: "CustomFeeds",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
