using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class StationMergeMovedRawStopJoinTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_FeedVersionId_TripId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_FeedVersions_FeedId_IsActive",
                table: "FeedVersions");

            migrationBuilder.DropIndex(
                name: "IX_FeedVersions_Sha1",
                table: "FeedVersions");

            migrationBuilder.AlterColumn<string>(
                name: "FeedId",
                table: "Feeds",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Alerts",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "StationMergeMovedRawStop",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationMergeLogId = table.Column<int>(type: "int", nullable: false),
                    RawStopId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationMergeMovedRawStop", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StationMergeMovedRawStop_StationMergeLogs_StationMergeLogId",
                        column: x => x.StationMergeLogId,
                        principalTable: "StationMergeLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_FeedVersionId_TripId",
                table: "Trips",
                columns: new[] { "FeedVersionId", "TripId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedVersions_FeedId_IsActive",
                table: "FeedVersions",
                columns: new[] { "FeedId", "IsActive" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_FeedVersions_Sha1",
                table: "FeedVersions",
                column: "Sha1",
                unique: true);

            // Deduplicate Feeds with same FeedId before creating unique index
            migrationBuilder.Sql(@"
                DECLARE @keepId int, @removeId int;
                DECLARE dup_cur CURSOR FOR
                    SELECT MIN(Id), MAX(Id) FROM Feeds GROUP BY FeedId HAVING COUNT(*) > 1;
                OPEN dup_cur;
                FETCH NEXT FROM dup_cur INTO @keepId, @removeId;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    UPDATE FeedVersions SET FeedId = @keepId WHERE FeedId = @removeId;
                    DELETE FROM Feeds WHERE Id = @removeId;
                    FETCH NEXT FROM dup_cur INTO @keepId, @removeId;
                END
                CLOSE dup_cur;
                DEALLOCATE dup_cur;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Feeds_FeedId",
                table: "Feeds",
                column: "FeedId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StationMergeMovedRawStop_StationMergeLogId",
                table: "StationMergeMovedRawStop",
                column: "StationMergeLogId");

            // Migrate existing MovedRawStopIds CSV data to the new join table
            migrationBuilder.Sql(@"
                INSERT INTO StationMergeMovedRawStop (StationMergeLogId, RawStopId)
                SELECT sml.Id, CAST(value AS int)
                FROM StationMergeLogs sml
                CROSS APPLY STRING_SPLIT(REPLACE(REPLACE(sml.MovedRawStopIds, '[', ''), ']', ''), ',')
                WHERE sml.MovedRawStopIds IS NOT NULL AND sml.MovedRawStopIds != ''
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StationMergeMovedRawStop");

            migrationBuilder.DropIndex(
                name: "IX_Trips_FeedVersionId_TripId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_FeedVersions_FeedId_IsActive",
                table: "FeedVersions");

            migrationBuilder.DropIndex(
                name: "IX_FeedVersions_Sha1",
                table: "FeedVersions");

            migrationBuilder.DropIndex(
                name: "IX_Feeds_FeedId",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Alerts");

            migrationBuilder.AlterColumn<string>(
                name: "FeedId",
                table: "Feeds",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_FeedVersionId_TripId",
                table: "Trips",
                columns: new[] { "FeedVersionId", "TripId" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedVersions_FeedId_IsActive",
                table: "FeedVersions",
                columns: new[] { "FeedId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedVersions_Sha1",
                table: "FeedVersions",
                column: "Sha1");
        }
    }
}
