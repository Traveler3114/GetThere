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
    /// <b>Hand-written.</b> Every other migration here came from <c>dotnet ef migrations add</c>;
    /// this one was authored by hand because the environment that produced it had no .NET SDK.
    /// Re-scaffolding it against a real toolchain before this reaches a shared database is the
    /// safe move — the risk is not the DDL below, which is two statements, but the model snapshot
    /// it has to agree with.
    /// </para>
    /// <para>
    /// The index is filtered on <c>ClientId IS NOT NULL</c> because SQL Server treats NULLs as equal
    /// in a unique index: unfiltered, a user could hold only one ticket created directly against the
    /// API. It is deliberately not filtered on <c>Status</c>, unlike the dedupe index beside it — a
    /// retry must find the original whatever became of it, including one already marked used.
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
