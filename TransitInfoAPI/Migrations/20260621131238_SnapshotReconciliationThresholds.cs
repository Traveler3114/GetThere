using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotReconciliationThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AutoMergeDistanceMetersAtDecision",
                table: "ReconciliationCandidates",
                type: "decimal(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoMergeNameThresholdAtDecision",
                table: "ReconciliationCandidates",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ManualReviewDistanceMetersAtDecision",
                table: "ReconciliationCandidates",
                type: "decimal(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ManualReviewNameThresholdAtDecision",
                table: "ReconciliationCandidates",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoMergeDistanceMetersAtDecision",
                table: "ReconciliationCandidates");

            migrationBuilder.DropColumn(
                name: "AutoMergeNameThresholdAtDecision",
                table: "ReconciliationCandidates");

            migrationBuilder.DropColumn(
                name: "ManualReviewDistanceMetersAtDecision",
                table: "ReconciliationCandidates");

            migrationBuilder.DropColumn(
                name: "ManualReviewNameThresholdAtDecision",
                table: "ReconciliationCandidates");
        }
    }
}
