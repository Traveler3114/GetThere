using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GetThereAPI.Migrations
{
    /// <summary>
    /// Adds <c>ImportedTickets.ClientId</c> — the idempotency key for the offline import queue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The index is filtered on <c>ClientId IS NOT NULL</c> because SQL Server treats NULLs as equal
    /// in a unique index: unfiltered, a user could hold only one ticket created directly against the
    /// API. It is deliberately not filtered on <c>Status</c>, unlike the dedupe index beside it — a
    /// retry must find the original whatever became of it, including one already marked used.
    /// </para>
    /// <para>
    /// This replaces a hand-written migration dated <c>20260731120000</c>, authored in an
    /// environment with no .NET SDK. The DDL was correct and is reproduced verbatim below, but the
    /// file carried no <c>[Migration]</c> attribute and no <c>.Designer.cs</c> — the attribute is
    /// how EF discovers migrations, so it was invisible to <c>migrations list</c> and silently
    /// skipped by <c>database update</c>. The column never reached any database, and every query
    /// touching <c>ImportedTickets</c> failed with <c>Invalid column name 'ClientId'</c>. This is
    /// the failure mode that makes "never hand-write a migration" worth enforcing: the DDL is the
    /// easy part to get right, and the metadata is the part that silently does nothing.
    /// </para>
    /// </remarks>
    public partial class AddImportedTicketClientId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "ImportedTickets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportedTickets_UserId_ClientId",
                table: "ImportedTickets",
                columns: new[] { "UserId", "ClientId" },
                unique: true,
                filter: "[ClientId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportedTickets_UserId_ClientId",
                table: "ImportedTickets");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "ImportedTickets");
        }
    }
}
