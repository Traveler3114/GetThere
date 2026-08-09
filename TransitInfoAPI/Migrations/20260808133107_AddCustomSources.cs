using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The five CustomFeed* tables are left over from the custom-feed pipeline removed in
            // 20a6e25. The squashed baseline dropped their entities but not the tables, so they have
            // been sitting in the live database holding data no code can read. The replacement
            // models the same problem differently (one request per TransitDocument section, and a
            // named C# extractor as the escape hatch), so there is nothing to migrate forward.
            //
            // Down() does not recreate them: they have no entities to recreate from, and restoring
            // empty tables would be worse than their absence. Take a backup first if the historical
            // run log matters.
            // Feeds.CustomFeedId outlived the entity property that mapped it — the column and its
            // foreign key are still in the live database, which is why dropping CustomFeeds fails
            // with "referenced by a FOREIGN KEY constraint" until this goes first. Feeds.CustomSourceId,
            // added below, replaces it. Guarded so the migration is a no-op on a database built from
            // the squashed baseline, where neither exists.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Feeds_CustomFeeds_CustomFeedId')
    ALTER TABLE Feeds DROP CONSTRAINT FK_Feeds_CustomFeeds_CustomFeedId;");
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Feeds_CustomFeedId' AND object_id = OBJECT_ID('Feeds'))
    DROP INDEX IX_Feeds_CustomFeedId ON Feeds;");
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Feeds') AND name = 'CustomFeedId')
    ALTER TABLE Feeds DROP COLUMN CustomFeedId;");

            // Children before parents; dropping a table takes its own inbound FKs with it.
            migrationBuilder.Sql("DROP TABLE IF EXISTS CustomFeedTableFieldMappings;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS CustomFeedFieldMappings;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS CustomFeedTableConfigs;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS CustomFeedRuns;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS CustomFeeds;");

            // Every existing version came from a GTFS archive, which by definition carries a full
            // timetable. The generated default was "", which is not a FeedCompleteness value and
            // would fail to materialise on read.
            migrationBuilder.AddColumn<string>(
                name: "Completeness",
                table: "FeedVersions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Schedule");

            migrationBuilder.AddColumn<string>(
                name: "SynthesizedSections",
                table: "FeedVersions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomSourceId",
                table: "Feeds",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperatorId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExtractorKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AuthConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefreshIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ServiceWindowStart = table.Column<DateOnly>(type: "date", nullable: true),
                    ServiceWindowEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomSources_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomSourceRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomSourceId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TargetSection = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HttpMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DataPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaginationConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DistinctBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomSourceRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomSourceRequests_CustomSources_CustomSourceId",
                        column: x => x.CustomSourceId,
                        principalTable: "CustomSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomSourceRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomSourceId = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RecordsProduced = table.Column<int>(type: "int", nullable: false),
                    LogText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FeedVersionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomSourceRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomSourceRuns_CustomSources_CustomSourceId",
                        column: x => x.CustomSourceId,
                        principalTable: "CustomSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomSourceRuns_FeedVersions_FeedVersionId",
                        column: x => x.FeedVersionId,
                        principalTable: "FeedVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomSourceMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomSourceRequestId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    SourceExpression = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    TargetField = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomSourceMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomSourceMappings_CustomSourceRequests_CustomSourceRequestId",
                        column: x => x.CustomSourceRequestId,
                        principalTable: "CustomSourceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Feeds_CustomSourceId",
                table: "Feeds",
                column: "CustomSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomSourceMappings_CustomSourceRequestId_SortOrder",
                table: "CustomSourceMappings",
                columns: new[] { "CustomSourceRequestId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomSourceRequests_CustomSourceId_SortOrder",
                table: "CustomSourceRequests",
                columns: new[] { "CustomSourceId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomSourceRuns_CustomSourceId_StartedAt",
                table: "CustomSourceRuns",
                columns: new[] { "CustomSourceId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomSourceRuns_FeedVersionId",
                table: "CustomSourceRuns",
                column: "FeedVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomSources_IsActive",
                table: "CustomSources",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CustomSources_OperatorId",
                table: "CustomSources",
                column: "OperatorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Feeds_CustomSources_CustomSourceId",
                table: "Feeds",
                column: "CustomSourceId",
                principalTable: "CustomSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Feeds_CustomSources_CustomSourceId",
                table: "Feeds");

            migrationBuilder.DropTable(
                name: "CustomSourceMappings");

            migrationBuilder.DropTable(
                name: "CustomSourceRuns");

            migrationBuilder.DropTable(
                name: "CustomSourceRequests");

            migrationBuilder.DropTable(
                name: "CustomSources");

            migrationBuilder.DropIndex(
                name: "IX_Feeds_CustomSourceId",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "Completeness",
                table: "FeedVersions");

            migrationBuilder.DropColumn(
                name: "SynthesizedSections",
                table: "FeedVersions");

            migrationBuilder.DropColumn(
                name: "CustomSourceId",
                table: "Feeds");
        }
    }
}
