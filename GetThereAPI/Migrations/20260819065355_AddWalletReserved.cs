using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletReserved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Reserved",
                table: "Wallets",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reserved",
                table: "Wallets");
        }
    }
}
