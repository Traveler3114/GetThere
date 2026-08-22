using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertSourceEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AlertSourceId",
                table: "Feeds",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AlertSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    ItemSelector = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    TitleSelector = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DescriptionSelector = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DateSelector = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    LinkSelector = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CategorySelector = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    IntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastItemCount = table.Column<int>(type: "int", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertSources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Feeds_AlertSourceId",
                table: "Feeds",
                column: "AlertSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertSources_SourceKey",
                table: "AlertSources",
                column: "SourceKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Feeds_AlertSources_AlertSourceId",
                table: "Feeds",
                column: "AlertSourceId",
                principalTable: "AlertSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Feeds_AlertSources_AlertSourceId",
                table: "Feeds");

            migrationBuilder.DropTable(
                name: "AlertSources");

            migrationBuilder.DropIndex(
                name: "IX_Feeds_AlertSourceId",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "AlertSourceId",
                table: "Feeds");
        }
    }
}
