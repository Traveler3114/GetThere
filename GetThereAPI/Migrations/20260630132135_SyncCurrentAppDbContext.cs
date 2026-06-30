using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <inheritdoc />
    public partial class SyncCurrentAppDbContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Purchases_WalletTransactionId",
                table: "Purchases",
                column: "WalletTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_WalletTransactions_WalletTransactionId",
                table: "Purchases",
                column: "WalletTransactionId",
                principalTable: "WalletTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_WalletTransactions_WalletTransactionId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_WalletTransactionId",
                table: "Purchases");
        }
    }
}
