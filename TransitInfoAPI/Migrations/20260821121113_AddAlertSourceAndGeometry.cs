using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertSourceAndGeometry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "FeedId",
                table: "Alerts",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "GeometryGeoJson",
                table: "Alerts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Alerts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Alerts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Alerts",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchedRouteIds",
                table: "Alerts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OperatorId",
                table: "Alerts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Severity",
                table: "Alerts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "Alerts",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "Alerts",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_Kind",
                table: "Alerts",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_OperatorId",
                table: "Alerts",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_SourceKey",
                table: "Alerts",
                column: "SourceKey");

            migrationBuilder.AddForeignKey(
                name: "FK_Alerts_Operators_OperatorId",
                table: "Alerts",
                column: "OperatorId",
                principalTable: "Operators",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Alerts_Operators_OperatorId",
                table: "Alerts");

            migrationBuilder.DropIndex(
                name: "IX_Alerts_Kind",
                table: "Alerts");

            migrationBuilder.DropIndex(
                name: "IX_Alerts_OperatorId",
                table: "Alerts");

            migrationBuilder.DropIndex(
                name: "IX_Alerts_SourceKey",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "GeometryGeoJson",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "MatchedRouteIds",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "OperatorId",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "Severity",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "Alerts");

            migrationBuilder.AlterColumn<int>(
                name: "FeedId",
                table: "Alerts",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
