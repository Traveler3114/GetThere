using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class ScopeFeedVersionShaToFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeedVersions_Sha1",
                table: "FeedVersions");

            migrationBuilder.CreateIndex(
                name: "IX_FeedVersions_FeedId_Sha1",
                table: "FeedVersions",
                columns: new[] { "FeedId", "Sha1" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedVersions_Sha1",
                table: "FeedVersions",
                column: "Sha1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeedVersions_FeedId_Sha1",
                table: "FeedVersions");

            migrationBuilder.DropIndex(
                name: "IX_FeedVersions_Sha1",
                table: "FeedVersions");

            migrationBuilder.CreateIndex(
                name: "IX_FeedVersions_Sha1",
                table: "FeedVersions",
                column: "Sha1",
                unique: true);
        }
    }
}
