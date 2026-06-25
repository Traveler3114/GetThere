using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCountryFromOperator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Strip country ISO prefix from existing GlobalIds: gt-{iso}-{name} → gt-{name}
            // and OnestopIds: o-{iso}-{slug} → o-{slug}
            migrationBuilder.Sql("""
                UPDATE Operators SET
                    GlobalId = 'gt-' + SUBSTRING(GlobalId, CHARINDEX('-', GlobalId, CHARINDEX('-', GlobalId) + 1) + 1, LEN(GlobalId)),
                    OnestopId = 'o-' + SUBSTRING(OnestopId, CHARINDEX('-', OnestopId, CHARINDEX('-', OnestopId) + 1) + 1, LEN(OnestopId))
                WHERE GlobalId LIKE 'gt-%-%'
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Operators_Countries_CountryId",
                table: "Operators");

            migrationBuilder.DropIndex(
                name: "IX_Operators_CountryId",
                table: "Operators");

            migrationBuilder.DropColumn(
                name: "CountryId",
                table: "Operators");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CountryId",
                table: "Operators",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Operators_CountryId",
                table: "Operators",
                column: "CountryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Operators_Countries_CountryId",
                table: "Operators",
                column: "CountryId",
                principalTable: "Countries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
