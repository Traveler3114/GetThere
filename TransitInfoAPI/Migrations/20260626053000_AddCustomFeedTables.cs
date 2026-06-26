using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFeedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomFeeds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperatorId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HttpMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OutputFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DataPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetTable = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaginationConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefreshIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    SourceExpression = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetField = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MappingKind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
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
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RecordsProduced = table.Column<int>(type: "int", nullable: false),
                    LogText = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomFeedFieldMappings");

            migrationBuilder.DropTable(
                name: "CustomFeedRuns");

            migrationBuilder.DropTable(
                name: "CustomFeeds");
        }
    }
}
