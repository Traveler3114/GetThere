using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransitInfoAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddStopTimeRawStopIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_StopTimes_RawStopId'
                      AND object_id = OBJECT_ID(N'[dbo].[StopTimes]')
                )
                BEGIN
                    CREATE INDEX [IX_StopTimes_RawStopId] ON [dbo].[StopTimes] ([RawStopId]);
                END
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StopTimes_RawStopId",
                table: "StopTimes");
        }
    }
}
