using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class MergeExternalInternalUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Merge ExternalUrl into InternalUrl first (InternalUrl still exists at this point)
            migrationBuilder.Sql("UPDATE Feeds SET InternalUrl = COALESCE(ExternalUrl, InternalUrl)");
            migrationBuilder.Sql("UPDATE MobilityProviders SET InternalUrl = COALESCE(ExternalUrl, InternalUrl)");

            migrationBuilder.DropColumn(
                name: "ExternalUrl",
                table: "MobilityProviders");

            migrationBuilder.DropColumn(
                name: "ExternalUrl",
                table: "Feeds");

            migrationBuilder.RenameColumn(
                name: "InternalUrl",
                table: "MobilityProviders",
                newName: "Url");

            migrationBuilder.RenameColumn(
                name: "InternalUrl",
                table: "Feeds",
                newName: "Url");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Url",
                table: "MobilityProviders",
                newName: "InternalUrl");

            migrationBuilder.RenameColumn(
                name: "Url",
                table: "Feeds",
                newName: "InternalUrl");

            migrationBuilder.AddColumn<string>(
                name: "ExternalUrl",
                table: "MobilityProviders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExternalUrl",
                table: "Feeds",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
