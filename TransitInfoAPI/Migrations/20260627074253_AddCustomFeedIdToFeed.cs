using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFeedIdToFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomFeedId",
                table: "Feeds",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Feeds_CustomFeedId",
                table: "Feeds",
                column: "CustomFeedId");

            migrationBuilder.AddForeignKey(
                name: "FK_Feeds_CustomFeeds_CustomFeedId",
                table: "Feeds",
                column: "CustomFeedId",
                principalTable: "CustomFeeds",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Feeds_CustomFeeds_CustomFeedId",
                table: "Feeds");

            migrationBuilder.DropIndex(
                name: "IX_Feeds_CustomFeedId",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "CustomFeedId",
                table: "Feeds");
        }
    }
}
