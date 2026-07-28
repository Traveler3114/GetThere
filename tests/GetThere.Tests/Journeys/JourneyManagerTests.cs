using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereAPI.Managers;

using GetThereShared.Contracts;
using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GetThere.Tests.Journeys;

/// <summary>
/// Journeys run on the in-memory provider, unlike the imported-ticket manager tests. Nothing here
/// depends on a filtered unique index or on SQL Server error numbers — the behaviour that matters
/// is ownership scoping and that deleting a grouping never takes its tickets with it, both of which
/// are enforced in code rather than by the database.
/// </summary>
public class JourneyManagerTests
{
    private const string Owner = "user-1";
    private const string Stranger = "user-2";

    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"journeys-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static JourneyManager Manager(AppDbContext db) => new(db, NullLogger<JourneyManager>.Instance);

    private static ImportedTicket Ticket(
        string userId = Owner,
        string? name = "Leg",
        string? origin = null,
        string? destination = null,
        DateTime? from = null,
        DateTime? to = null,
        ImportedTicketStatus status = ImportedTicketStatus.Active) => new()
        {
            UserId = userId,
            TicketName = name,
            OriginName = origin,
            DestinationName = destination,
            ValidFrom = from,
            ValidTo = to,
            Status = status,
            Source = ImportSource.Manual
        };

    // ── Ownership and membership ────────────────────────────────────────────────────

    [Fact]
    public async Task Creates_a_journey_with_legs_and_derives_its_window()
    {
        using var db = NewContext();
        var first = Ticket(from: new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc));
        var second = Ticket(from: new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 16, 18, 0, 0, DateTimeKind.Utc));
        db.ImportedTickets.AddRange(first, second);
        await db.SaveChangesAsync();

        var result = await Manager(db).CreateAsync(Owner, new CreateJourneyRequest
        {
            Name = "Summer trip",
            ImportedTicketIds = [first.Id, second.Id]
        });

        Assert.Equal(2, result.LegCount);
        // The window spans the earliest departure to the latest arrival across all legs.
        Assert.Equal(new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc), result.StartsAt);
        Assert.Equal(new DateTime(2026, 8, 16, 18, 0, 0, DateTimeKind.Utc), result.EndsAt);
    }

    [Fact]
    public async Task Legs_come_back_in_time_order()
    {
        using var db = NewContext();
        var later = Ticket(name: "Return", from: new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc));
        var earlier = Ticket(name: "Outbound", from: new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc));
        db.ImportedTickets.AddRange(later, earlier);
        await db.SaveChangesAsync();

        var result = await Manager(db).CreateAsync(Owner, new CreateJourneyRequest
        {
            Name = "Trip",
            ImportedTicketIds = [later.Id, earlier.Id]
        });

        Assert.Equal(["Outbound", "Return"], result.Legs.Select(l => l.TicketName));
    }

    [Fact]
    public async Task Cannot_put_another_users_ticket_into_a_journey()
    {
        using var db = NewContext();
        var theirs = Ticket(userId: Stranger);
        db.ImportedTickets.Add(theirs);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<AppException>(() =>
            Manager(db).CreateAsync(Owner, new CreateJourneyRequest { Name = "Trip", ImportedTicketIds = [theirs.Id] }));

        Assert.Equal(404, error.StatusCode);
        // And it must not have half-applied.
        Assert.Null((await db.ImportedTickets.FindAsync(theirs.Id))!.JourneyId);
    }

    [Fact]
    public async Task Cannot_read_another_users_journey()
    {
        using var db = NewContext();
        var created = await Manager(db).CreateAsync(Owner, new CreateJourneyRequest { Name = "Mine" });

        Assert.Null(await Manager(db).GetByIdAsync(created.Id, Stranger));
    }

    [Fact]
    public async Task Cannot_delete_another_users_journey()
    {
        using var db = NewContext();
        var created = await Manager(db).CreateAsync(Owner, new CreateJourneyRequest { Name = "Mine" });

        await Assert.ThrowsAsync<AppException>(() => Manager(db).DeleteAsync(created.Id, Stranger));
        Assert.NotNull(await Manager(db).GetByIdAsync(created.Id, Owner));
    }

    /// <summary>
    /// The whole point of the SetNull foreign key: the tickets are the valuable thing, the grouping
    /// is not. Losing a trip must never lose the tickets in it.
    /// </summary>
    [Fact]
    public async Task Deleting_a_journey_releases_its_tickets_rather_than_deleting_them()
    {
        using var db = NewContext();
        var ticket = Ticket(from: DateTime.UtcNow);
        db.ImportedTickets.Add(ticket);
        await db.SaveChangesAsync();

        var journey = await Manager(db).CreateAsync(Owner, new CreateJourneyRequest
        {
            Name = "Trip",
            ImportedTicketIds = [ticket.Id]
        });

        await Manager(db).DeleteAsync(journey.Id, Owner);

        var survivor = await db.ImportedTickets.FindAsync(ticket.Id);
        Assert.NotNull(survivor);
        Assert.Null(survivor!.JourneyId);
        Assert.Empty(db.Journeys);
    }

    [Fact]
    public async Task Removing_a_ticket_updates_the_window()
    {
        using var db = NewContext();
        var keep = Ticket(from: new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc));
        var drop = Ticket(from: new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 20, 18, 0, 0, DateTimeKind.Utc));
        db.ImportedTickets.AddRange(keep, drop);
        await db.SaveChangesAsync();

        var manager = Manager(db);
        var journey = await manager.CreateAsync(Owner, new CreateJourneyRequest
        {
            Name = "Trip",
            ImportedTicketIds = [keep.Id, drop.Id]
        });

        var updated = await manager.RemoveTicketsAsync(journey.Id, Owner, new JourneyMembershipRequest { ImportedTicketIds = [drop.Id] });

        Assert.Equal(1, updated.LegCount);
        Assert.Equal(new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc), updated.EndsAt);
    }

    /// <summary>
    /// Planned/Active/Completed are recomputed from the legs, so accepting them from a client would
    /// only store a value the next sweep overwrites.
    /// </summary>
    [Fact]
    public async Task Only_cancellation_can_be_set_by_hand()
    {
        using var db = NewContext();
        var manager = Manager(db);
        var journey = await manager.CreateAsync(Owner, new CreateJourneyRequest { Name = "Trip" });

        await Assert.ThrowsAsync<AppException>(() =>
            manager.UpdateAsync(journey.Id, Owner, new UpdateJourneyRequest { Status = JourneyStatus.Completed }));

        var cancelled = await manager.UpdateAsync(journey.Id, Owner, new UpdateJourneyRequest { Status = JourneyStatus.Cancelled });
        Assert.Equal(JourneyStatus.Cancelled, cancelled.Status);
    }

    // ── Status rollup ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_rolls_forward_from_the_legs()
    {
        using var db = NewContext();
        var past = Ticket(from: DateTime.UtcNow.AddDays(-5), to: DateTime.UtcNow.AddDays(-4));
        var future = Ticket(from: DateTime.UtcNow.AddDays(5), to: DateTime.UtcNow.AddDays(6));
        var current = Ticket(from: DateTime.UtcNow.AddHours(-1), to: DateTime.UtcNow.AddHours(5));
        db.ImportedTickets.AddRange(past, future, current);
        await db.SaveChangesAsync();

        var manager = Manager(db);
        var completed = await manager.CreateAsync(Owner, new CreateJourneyRequest { Name = "Done", ImportedTicketIds = [past.Id] });
        var planned = await manager.CreateAsync(Owner, new CreateJourneyRequest { Name = "Upcoming", ImportedTicketIds = [future.Id] });
        var active = await manager.CreateAsync(Owner, new CreateJourneyRequest { Name = "Now", ImportedTicketIds = [current.Id] });

        await manager.RollUpStatusesAsync();

        Assert.Equal(JourneyStatus.Completed, (await manager.GetByIdAsync(completed.Id, Owner))!.Status);
        Assert.Equal(JourneyStatus.Planned, (await manager.GetByIdAsync(planned.Id, Owner))!.Status);
        Assert.Equal(JourneyStatus.Active, (await manager.GetByIdAsync(active.Id, Owner))!.Status);
    }

    [Fact]
    public async Task Rollup_leaves_a_cancelled_journey_alone()
    {
        using var db = NewContext();
        var past = Ticket(from: DateTime.UtcNow.AddDays(-5), to: DateTime.UtcNow.AddDays(-4));
        db.ImportedTickets.Add(past);
        await db.SaveChangesAsync();

        var manager = Manager(db);
        var journey = await manager.CreateAsync(Owner, new CreateJourneyRequest { Name = "Abandoned", ImportedTicketIds = [past.Id] });
        await manager.UpdateAsync(journey.Id, Owner, new UpdateJourneyRequest { Status = JourneyStatus.Cancelled });

        await manager.RollUpStatusesAsync();

        Assert.Equal(JourneyStatus.Cancelled, (await manager.GetByIdAsync(journey.Id, Owner))!.Status);
    }

    // ── Suggestions ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Suggests_tickets_that_chain_end_to_end()
    {
        using var db = NewContext();
        db.ImportedTickets.AddRange(
            Ticket(name: "Out", origin: "Zagreb", destination: "Vienna", from: new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 14, 14, 0, 0, DateTimeKind.Utc)),
            // Six days later, so only the route chain can group these — the time window cannot.
            Ticket(name: "Back", origin: "Vienna", destination: "Zagreb", from: new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 20, 15, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var suggestions = await Manager(db).SuggestAsync(Owner);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal(2, suggestion.Legs.Count);
        Assert.Equal("Zagreb → Zagreb", suggestion.SuggestedName);
        Assert.Contains("connect", suggestion.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Suggests_tickets_that_sit_close_together_in_time()
    {
        using var db = NewContext();
        db.ImportedTickets.AddRange(
            Ticket(name: "Train", from: new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc)),
            Ticket(name: "Tram day pass", from: new DateTime(2026, 8, 14, 13, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 14, 23, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var suggestions = await Manager(db).SuggestAsync(Owner);

        Assert.Single(suggestions);
        Assert.Equal(2, suggestions[0].Legs.Count);
    }

    [Fact]
    public async Task A_lone_ticket_is_not_a_trip()
    {
        using var db = NewContext();
        db.ImportedTickets.Add(Ticket(from: new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        Assert.Empty(await Manager(db).SuggestAsync(Owner));
    }

    [Fact]
    public async Task Tickets_far_apart_and_unconnected_are_not_grouped()
    {
        using var db = NewContext();
        db.ImportedTickets.AddRange(
            Ticket(name: "August", origin: "Zagreb", destination: "Split", from: new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc)),
            Ticket(name: "December", origin: "Rijeka", destination: "Pula", from: new DateTime(2026, 12, 20, 8, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 12, 20, 12, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        Assert.Empty(await Manager(db).SuggestAsync(Owner));
    }

    [Fact]
    public async Task Tickets_already_in_a_journey_are_not_suggested_again()
    {
        using var db = NewContext();
        var a = Ticket(from: new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc));
        var b = Ticket(from: new DateTime(2026, 8, 14, 13, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 14, 20, 0, 0, DateTimeKind.Utc));
        db.ImportedTickets.AddRange(a, b);
        await db.SaveChangesAsync();

        var manager = Manager(db);
        await manager.CreateAsync(Owner, new CreateJourneyRequest { Name = "Already grouped", ImportedTicketIds = [a.Id, b.Id] });

        Assert.Empty(await manager.SuggestAsync(Owner));
    }

    [Fact]
    public async Task Cancelled_tickets_are_not_suggested()
    {
        using var db = NewContext();
        db.ImportedTickets.AddRange(
            Ticket(from: new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc), status: ImportedTicketStatus.Cancelled),
            Ticket(from: new DateTime(2026, 8, 14, 13, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 14, 20, 0, 0, DateTimeKind.Utc), status: ImportedTicketStatus.Cancelled));
        await db.SaveChangesAsync();

        Assert.Empty(await Manager(db).SuggestAsync(Owner));
    }

    [Fact]
    public async Task One_users_tickets_never_appear_in_anothers_suggestions()
    {
        using var db = NewContext();
        db.ImportedTickets.AddRange(
            Ticket(userId: Stranger, from: new DateTime(2026, 8, 14, 8, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc)),
            Ticket(userId: Stranger, from: new DateTime(2026, 8, 14, 13, 0, 0, DateTimeKind.Utc), to: new DateTime(2026, 8, 14, 20, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        Assert.Empty(await Manager(db).SuggestAsync(Owner));
    }

    // ── Station-name matching ───────────────────────────────────────────────────────

    /// <summary>
    /// The two names come from different operators' formatting of the same place, so matching is
    /// deliberately loose — otherwise a return trip never chains.
    /// </summary>
    [Theory]
    [InlineData("Vienna", "Vienna", true)]
    [InlineData("Zagreb Gl. Kol.", "Zagreb Glavni Kolodvor", true)]
    [InlineData("vienna", "VIENNA", true)]
    [InlineData("Zagreb", "Zagreb Glavni", true)]
    [InlineData("Vienna", "Zagreb", false)]
    [InlineData("Split", "Rijeka", false)]
    [InlineData(null, "Vienna", false)]
    [InlineData("Vienna", null, false)]
    [InlineData("", "Vienna", false)]
    public void Matches_station_names_across_operator_formatting(string? destination, string? origin, bool expected)
    {
        Assert.Equal(expected, JourneyManager.Connects(destination, origin));
    }
}
