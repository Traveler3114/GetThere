using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFeedMobilityProviderId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MobilityProviderId",
                table: "CustomFeeds",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomFeeds_MobilityProviderId",
                table: "CustomFeeds",
                column: "MobilityProviderId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomFeeds_MobilityProviders_MobilityProviderId",
                table: "CustomFeeds",
                column: "MobilityProviderId",
                principalTable: "MobilityProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomFeeds_MobilityProviders_MobilityProviderId",
                table: "CustomFeeds");

            migrationBuilder.DropIndex(
                name: "IX_CustomFeeds_MobilityProviderId",
                table: "CustomFeeds");

            migrationBuilder.DropColumn(
                name: "MobilityProviderId",
                table: "CustomFeeds");
        }
    }
}
