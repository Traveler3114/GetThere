using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Managers;

using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GetThere.Tests.Search;

/// <summary>
/// Search filters on <see cref="ImportedTicketManager"/> and <see cref="JourneyManager"/> are
/// translated to SQL (<c>WHERE ... LIKE '%term%'</c>) and rely on the database collation for
/// case-insensitivity. The in-memory provider evaluates <c>Where</c> client-side and would
/// happily execute <c>ToLowerInvariant()</c> or any other non-translatable call, so a green
/// build with in-memory tests would hide a query that throws at runtime on SQL Server — exactly
/// what happened before <c>de1ccaf</c>. Every test in this class therefore runs against a real
/// SQL Server database (via <see cref="SearchFixture"/> / <see cref="TestDatabase"/>), not
/// <c>UseInMemoryDatabase</c>.
/// </summary>
[Collection(SearchCollection.Name)]
public class SearchFilterTests
{
    private static async Task<string> CreateUserAsync(AppDbContext db)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"search-{Guid.NewGuid():N}@example.com",
            Email = $"search-{Guid.NewGuid():N}@example.com",
            FullName = "Search Probe"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static ImportedTicket Ticket(string userId, string? ticketName = null, string? routeDescription = null, string? originName = null, string? destinationName = null, string? operatorNameSnapshot = null)
        => new()
        {
            UserId = userId,
            TicketName = ticketName,
            RouteDescription = routeDescription,
            OriginName = originName,
            DestinationName = destinationName,
            OperatorNameSnapshot = operatorNameSnapshot,
            Source = ImportSource.Manual,
            Status = ImportedTicketStatus.Active
        };

    // ── ImportedTicketManager ─────────────────────────────────────────────────────

    [Fact]
    public async Task ImportedTicket_search_matches_ticket_name()
    {
        using var db = SearchFixture.CreateContext();
        var userId = await CreateUserAsync(db);
        var token = $"TicketName_{Guid.NewGuid():N}";
        db.ImportedTickets.Add(Ticket(userId, ticketName: token));
        db.ImportedTickets.Add(Ticket(userId, ticketName: "other"));
        await db.SaveChangesAsync();

        var manager = new ImportedTicketManager(db, NullLogger<ImportedTicketManager>.Instance);
        var result = await manager.ListAsync(userId, page: 1, perPage: 50, search: token);

        Assert.Single(result.Data);
        Assert.Equal(token, result.Data[0].TicketName);
    }

    [Fact]
    public async Task ImportedTicket_search_matches_route_description()
    {
        using var db = SearchFixture.CreateContext();
        var userId = await CreateUserAsync(db);
        var token = $"RouteDesc_{Guid.NewGuid():N}";
        db.ImportedTickets.Add(Ticket(userId, routeDescription: $"via {token} express"));
        db.ImportedTickets.Add(Ticket(userId, routeDescription: "other route"));
        await db.SaveChangesAsync();

        var manager = new ImportedTicketManager(db, NullLogger<ImportedTicketManager>.Instance);
        var result = await manager.ListAsync(userId, page: 1, perPage: 50, search: token);

        Assert.Single(result.Data);
        Assert.Contains(token, result.Data[0].RouteDescription);
    }

    [Fact]
    public async Task ImportedTicket_search_matches_origin_name()
    {
        using var db = SearchFixture.CreateContext();
        var userId = await CreateUserAsync(db);
        var token = $"Origin_{Guid.NewGuid():N}";
        db.ImportedTickets.Add(Ticket(userId, originName: token));
        db.ImportedTickets.Add(Ticket(userId, originName: "other"));
        await db.SaveChangesAsync();

        var manager = new ImportedTicketManager(db, NullLogger<ImportedTicketManager>.Instance);
        var result = await manager.ListAsync(userId, page: 1, perPage: 50, search: token);

        Assert.Single(result.Data);
        Assert.Equal(token, result.Data[0].OriginName);
    }

    [Fact]
    public async Task ImportedTicket_search_matches_destination_name()
    {
        using var db = SearchFixture.CreateContext();
        var userId = await CreateUserAsync(db);
        var token = $"Dest_{Guid.NewGuid():N}";
        db.ImportedTickets.Add(Ticket(userId, destinationName: token));
        db.ImportedTickets.Add(Ticket(userId, destinationName: "other"));
        await db.SaveChangesAsync();

        var manager = new ImportedTicketManager(db, NullLogger<ImportedTicketManager>.Instance);
        var result = await manager.ListAsync(userId, page: 1, perPage: 50, search: token);

        Assert.Single(result.Data);
        Assert.Equal(token, result.Data[0].DestinationName);
    }

    [Fact]
    public async Task ImportedTicket_search_matches_operator_name_snapshot()
    {
        using var db = SearchFixture.CreateContext();
        var userId = await CreateUserAsync(db);
        var token = $"Operator_{Guid.NewGuid():N}";
        db.ImportedTickets.Add(Ticket(userId, operatorNameSnapshot: token));
        db.ImportedTickets.Add(Ticket(userId, operatorNameSnapshot: "other"));
        await db.SaveChangesAsync();

        var manager = new ImportedTicketManager(db, NullLogger<ImportedTicketManager>.Instance);
        var result = await manager.ListAsync(userId, page: 1, perPage: 50, search: token);

        Assert.Single(result.Data);
        Assert.Equal(token, result.Data[0].OperatorNameSnapshot);
    }

    [Fact]
    public async Task ImportedTicket_search_is_case_insensitive()
    {
        using var db = SearchFixture.CreateContext();
        var userId = await CreateUserAsync(db);
        var baseName = $"Zagreb_{Guid.NewGuid():N}";
        db.ImportedTickets.Add(Ticket(userId, ticketName: baseName));
        await db.SaveChangesAsync();

        var manager = new ImportedTicketManager(db, NullLogger<ImportedTicketManager>.Instance);

        var lower = await manager.ListAsync(userId, page: 1, perPage: 50, search: baseName.ToLowerInvariant());
        var upper = await manager.ListAsync(userId, page: 1, perPage: 50, search: baseName.ToUpperInvariant());
        var mixed = await manager.ListAsync(userId, page: 1, perPage: 50, search: baseName);

        Assert.Single(lower.Data);
        Assert.Single(upper.Data);
        Assert.Single(mixed.Data);
        Assert.Equal(lower.Data[0].Id, upper.Data[0].Id);
        Assert.Equal(lower.Data[0].Id, mixed.Data[0].Id);

        // Classic city-name triplet: stored as mixed case, searched in different casings
        var cityUser = await CreateUserAsync(db);
        db.ImportedTickets.Add(Ticket(cityUser, ticketName: "Zagreb Central"));
        await db.SaveChangesAsync();
        var mgr2 = new ImportedTicketManager(db, NullLogger<ImportedTicketManager>.Instance);
        var cityLow = await mgr2.ListAsync(cityUser, page: 1, perPage: 50, search: "zAgReB");
        var cityUp = await mgr2.ListAsync(cityUser, page: 1, perPage: 50, search: "ZAGREB");
        var cityExact = await mgr2.ListAsync(cityUser, page: 1, perPage: 50, search: "Zagreb");
        Assert.Single(cityLow.Data);
        Assert.Single(cityUp.Data);
        Assert.Single(cityExact.Data);
        Assert.Equal(cityLow.Data[0].Id, cityUp.Data[0].Id);
        Assert.Equal(cityLow.Data[0].Id, cityExact.Data[0].Id);
    }

    [Fact]
    public async Task ImportedTicket_search_returns_empty_when_no_match()
    {
        using var db = SearchFixture.CreateContext();
        var userId = await CreateUserAsync(db);
        db.ImportedTickets.Add(Ticket(userId, ticketName: $"Keep_{Guid.NewGuid():N}"));
        await db.SaveChangesAsync();

        var manager = new ImportedTicketManager(db, NullLogger<ImportedTicketManager>.Instance);
        var result = await manager.ListAsync(userId, page: 1, perPage: 50, search: $"NoMatch_{Guid.NewGuid():N}");

        Assert.Empty(result.Data);
        Assert.Equal(0, result.Total);
    }

    // ── JourneyManager ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Journey_search_matches_name()
    {
        using var db = SearchFixture.CreateContext();
        var userId = await CreateUserAsync(db);
        var token = $"JourneyName_{Guid.NewGuid():N}";
        db.Journeys.Add(new Journey { UserId = userId, Name = token, Status = GetThereShared.Enums.JourneyStatus.Planned });
        db.Journeys.Add(new Journey { UserId = userId, Name = "other", Status = GetThereShared.Enums.JourneyStatus.Planned });
        await db.SaveChangesAsync();

        var manager = new JourneyManager(db, NullLogger<JourneyManager>.Instance);
        var result = await manager.ListAsync(userId, page: 1, perPage: 50, search: token);

        Assert.Single(result.Data);
        Assert.Equal(token, result.Data[0].Name);
    }

    [Fact]
    public async Task Journey_search_matches_notes()
    {
        using var db = SearchFixture.CreateContext();
        var userId = await CreateUserAsync(db);
        var token = $"Notes_{Guid.NewGuid():N}";
        db.Journeys.Add(new Journey { UserId = userId, Name = "Trip", Notes = $"contains {token} inside", Status = GetThereShared.Enums.JourneyStatus.Planned });
        db.Journeys.Add(new Journey { UserId = userId, Name = "other", Notes = "other notes", Status = GetThereShared.Enums.JourneyStatus.Planned });
        await db.SaveChangesAsync();

        var manager = new JourneyManager(db, NullLogger<JourneyManager>.Instance);
        var result = await manager.ListAsync(userId, page: 1, perPage: 50, search: token);

        Assert.Single(result.Data);
        Assert.Contains(token, result.Data[0].Notes);
    }

    [Fact]
    public async Task Journey_search_is_case_insensitive()
    {
        using var db = SearchFixture.CreateContext();
        var userId = await CreateUserAsync(db);
        var name = $"Split_{Guid.NewGuid():N}";
        db.Journeys.Add(new Journey { UserId = userId, Name = name, Status = GetThereShared.Enums.JourneyStatus.Planned });
        await db.SaveChangesAsync();

        var manager = new JourneyManager(db, NullLogger<JourneyManager>.Instance);

        var lower = await manager.ListAsync(userId, page: 1, perPage: 50, search: name.ToLowerInvariant());
        var upper = await manager.ListAsync(userId, page: 1, perPage: 50, search: name.ToUpperInvariant());
        var mixed = await manager.ListAsync(userId, page: 1, perPage: 50, search: name);

        Assert.Single(lower.Data);
        Assert.Single(upper.Data);
        Assert.Single(mixed.Data);
        Assert.Equal(lower.Data[0].Id, upper.Data[0].Id);
        Assert.Equal(lower.Data[0].Id, mixed.Data[0].Id);

        // Notes case-insensitivity
        var notesUser = await CreateUserAsync(db);
        var notesToken = "Split";
        var notesJourney = new Journey { UserId = notesUser, Name = "Trip", Notes = $"Trip to {notesToken} city", Status = GetThereShared.Enums.JourneyStatus.Planned };
        db.Journeys.Add(notesJourney);
        await db.SaveChangesAsync();
        var mgr2 = new JourneyManager(db, NullLogger<JourneyManager>.Instance);
        var n1 = await mgr2.ListAsync(notesUser, page: 1, perPage: 50, search: "SPLIT");
        var n2 = await mgr2.ListAsync(notesUser, page: 1, perPage: 50, search: "split");
        var n3 = await mgr2.ListAsync(notesUser, page: 1, perPage: 50, search: "Split");
        Assert.Single(n1.Data);
        Assert.Single(n2.Data);
        Assert.Single(n3.Data);
        Assert.Equal(n1.Data[0].Id, n2.Data[0].Id);
        Assert.Equal(n1.Data[0].Id, n3.Data[0].Id);
    }

    [Fact]
    public async Task Journey_search_returns_empty_when_no_match()
    {
        using var db = SearchFixture.CreateContext();
        var userId = await CreateUserAsync(db);
        db.Journeys.Add(new Journey { UserId = userId, Name = $"Keep_{Guid.NewGuid():N}", Status = GetThereShared.Enums.JourneyStatus.Planned });
        await db.SaveChangesAsync();

        var manager = new JourneyManager(db, NullLogger<JourneyManager>.Instance);
        var result = await manager.ListAsync(userId, page: 1, perPage: 50, search: $"NoMatch_{Guid.NewGuid():N}");

        Assert.Empty(result.Data);
        Assert.Equal(0, result.Total);
    }
}
