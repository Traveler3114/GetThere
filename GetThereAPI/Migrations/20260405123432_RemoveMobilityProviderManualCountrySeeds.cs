using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMobilityProviderManualCountrySeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "MobilityProviderCity",
                keyColumns: new[] { "CitiesId", "MobilityProvidersId" },
                keyValues: new object[] { 1, 1 });

            migrationBuilder.DeleteData(
                table: "MobilityProviderCountry",
                keyColumns: new[] { "CountriesId", "MobilityProvidersId" },
                keyValues: new object[] { 1, 1 });

            migrationBuilder.UpdateData(
                table: "MobilityProviders",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "Bajs / Nextbike");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MobilityProviderCity",
                columns: new[] { "CitiesId", "MobilityProvidersId" },
                values: new object[] { 1, 1 });

            migrationBuilder.InsertData(
                table: "MobilityProviderCountry",
                columns: new[] { "CountriesId", "MobilityProvidersId" },
                values: new object[] { 1, 1 });

            migrationBuilder.UpdateData(
                table: "MobilityProviders",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "Bajs / Nextbike Zagreb");
        }
    }
}
