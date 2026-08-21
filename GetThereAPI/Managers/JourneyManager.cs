using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;

using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

/// <summary>
/// Groups tickets into trips. Membership spans both ticket tables, because a real journey mixes
/// tickets bought in the app with tickets imported from elsewhere.
/// </summary>
public class JourneyManager
{
    private readonly AppDbContext _db;
    private readonly ILogger<JourneyManager> _logger;

    public JourneyManager(AppDbContext db, ILogger<JourneyManager> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>How far apart two legs can sit and still be suggested as one trip.</summary>
    public static readonly TimeSpan SuggestionWindow = TimeSpan.FromHours(24);

    /// <summary>How many journeys one pass of the status sweep reads at a time.</summary>
    private const int RollUpBatchSize = 500;

    public async Task<PagedResult<JourneyResponse>> ListAsync(string userId, int page, int perPage, JourneyStatus? status = null, string? search = null, string? sort = null, CancellationToken ct = default)
    {
        var query = _db.Journeys.Where(j => j.UserId == userId);

        if (status.HasValue)
            query = query.Where(j => j.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(j => j.Name.ToLower().Contains(term) || (j.Notes != null && j.Notes.ToLower().Contains(term)));
        }

        // Sorting: default keeps upcoming-first semantics; explicit sort overrides it.
        query = sort?.ToLowerInvariant() switch
        {
            "name" => query.OrderBy(j => j.Name),
            "-name" => query.OrderByDescending(j => j.Name),
            "startsat" => query.OrderBy(j => j.StartsAt),
            "-startsat" => query.OrderByDescending(j => j.StartsAt),
            "createdat" => query.OrderBy(j => j.CreatedAt),
            "-createdat" => query.OrderByDescending(j => j.CreatedAt),
            "legcount" => query.OrderBy(j => j.ImportedTickets.Count + j.Tickets.Count),
            "-legcount" => query.OrderByDescending(j => j.ImportedTickets.Count + j.Tickets.Count),
            _ => query.OrderByDescending(j => j.StartsAt == null).ThenBy(j => j.StartsAt)
        };

        var total = await query.CountAsync(ct);

        // Legs are not loaded on list — one projection per journey. FirstOrigin/LastDestination
        // are derived from the earliest/latest leg dates so the list can show a route preview.
        var items = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(j => new JourneyResponse
            {
                Id = j.Id,
                Name = j.Name,
                Notes = j.Notes,
                Status = j.Status,
                StartsAt = j.StartsAt,
                EndsAt = j.EndsAt,
                LegCount = j.ImportedTickets.Count + j.Tickets.Count,
                FirstOrigin = j.ImportedTickets.OrderBy(t => t.ValidFrom).Select(t => t.OriginName).FirstOrDefault()
                              ?? j.Tickets.OrderBy(t => t.ValidFrom).Select(t => t.ExternalTicketId).FirstOrDefault(),
                LastDestination = j.ImportedTickets.OrderByDescending(t => t.ValidTo ?? t.ValidFrom).Select(t => t.DestinationName).FirstOrDefault()
                                  ?? j.Tickets.OrderByDescending(t => t.ValidTo ?? t.ValidFrom).Select(t => t.ExternalTicketId).FirstOrDefault(),
                CreatedAt = j.CreatedAt,
                UpdatedAt = j.UpdatedAt
            })
            .ToListAsync(ct);

        return new PagedResult<JourneyResponse>(items, total, page, perPage);
    }

    public async Task<JourneyResponse?> GetByIdAsync(int id, string userId, CancellationToken ct = default)
    {
        var journey = await _db.Journeys
            .Include(j => j.ImportedTickets)
            .Include(j => j.Tickets)
            .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, ct);

        return journey is null ? null : ToResponse(journey, includeLegs: true);
    }

    public async Task<JourneyResponse> CreateAsync(string userId, CreateJourneyRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new AppException("A journey needs a name.", 400);

        var journey = new Journey
        {
            UserId = userId,
            Name = request.Name.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Status = JourneyStatus.Planned,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Journeys.Add(journey);
        await _db.SaveChangesAsync(ct);

        if (request.ImportedTicketIds.Count > 0 || request.TicketIds.Count > 0)
        {
            await AssignAsync(userId, journey, request.ImportedTicketIds, request.TicketIds, ct);
            await _db.SaveChangesAsync(ct);
        }

        await RefreshDatesAsync(journey, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Journey {Id} created for user {UserId} with {Legs} legs",
            journey.Id, userId, request.ImportedTicketIds.Count + request.TicketIds.Count);

        return await GetByIdAsync(journey.Id, userId, ct)
            ?? throw new AppException("Journey could not be loaded after creation.", 500);
    }

    public async Task<JourneyResponse> UpdateAsync(int id, string userId, UpdateJourneyRequest request, CancellationToken ct = default)
    {
        var journey = await LoadOwnedAsync(id, userId, ct);

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new AppException("A journey needs a name.", 400);
            journey.Name = request.Name.Trim();
        }

        if (request.Notes is not null)
            journey.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        if (request.Status.HasValue)
        {
            // Planned/Active/Completed are a function of the legs' dates and get recomputed by the
            // expiry sweep, so letting a client set them would only produce a value that silently
            // reverts. Cancelled is the one genuine user decision.
            if (request.Status.Value != JourneyStatus.Cancelled)
                throw new AppException("Only cancellation can be set by hand — a journey's other states follow its tickets.", 400);
            journey.Status = JourneyStatus.Cancelled;
        }

        journey.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await GetByIdAsync(id, userId, ct)!
            ?? throw new AppException("Journey not found.", 404);
    }

    /// <summary>
    /// Deletes the journey and releases its tickets. The tickets are the valuable thing; the
    /// grouping is not, so this must never cascade into them.
    /// </summary>
    public async Task DeleteAsync(int id, string userId, CancellationToken ct = default)
    {
        var journey = await _db.Journeys
            .Include(j => j.ImportedTickets)
            .Include(j => j.Tickets)
            .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, ct);

        if (journey is null)
            throw new AppException("Journey not found.", 404);

        // Detached explicitly rather than relying on the SetNull foreign key alone, so the
        // behaviour holds even against a provider that does not apply it.
        foreach (var ticket in journey.ImportedTickets) ticket.JourneyId = null;
        foreach (var ticket in journey.Tickets) ticket.JourneyId = null;

        _db.Journeys.Remove(journey);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Journey {Id} deleted by user {UserId}; {Legs} tickets released",
            id, userId, journey.ImportedTickets.Count + journey.Tickets.Count);
    }

    public async Task<JourneyResponse> AddTicketsAsync(int id, string userId, JourneyMembershipRequest request, CancellationToken ct = default)
    {
        var journey = await LoadOwnedAsync(id, userId, ct);

        await AssignAsync(userId, journey, request.ImportedTicketIds, request.TicketIds, ct);

        // Membership is persisted before the window is recomputed: RefreshDatesAsync queries the
        // tickets back out, so pending in-memory changes would not be visible to it.
        await _db.SaveChangesAsync(ct);

        await RefreshDatesAsync(journey, ct);
        journey.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await GetByIdAsync(id, userId, ct)!
            ?? throw new AppException("Journey not found.", 404);
    }

    public async Task<JourneyResponse> RemoveTicketsAsync(int id, string userId, JourneyMembershipRequest request, CancellationToken ct = default)
    {
        var journey = await LoadOwnedAsync(id, userId, ct);

        var imported = await _db.ImportedTickets
            .Where(t => t.JourneyId == id && t.UserId == userId && request.ImportedTicketIds.Contains(t.Id))
            .ToListAsync(ct);
        foreach (var ticket in imported) ticket.JourneyId = null;

        var purchased = await OwnedTicketsQuery(userId)
            .Where(t => t.JourneyId == id && request.TicketIds.Contains(t.Id))
            .ToListAsync(ct);
        foreach (var ticket in purchased) ticket.JourneyId = null;

        // Same ordering as AddTicketsAsync — the removals must be persisted before the window is
        // recomputed from a fresh query, or it still reflects the tickets just taken out.
        await _db.SaveChangesAsync(ct);

        await RefreshDatesAsync(journey, ct);
        journey.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await GetByIdAsync(id, userId, ct)!
            ?? throw new AppException("Journey not found.", 404);
    }

    /// <summary>
    /// Proposes groupings over tickets that are not in a journey yet. Suggestions are returned for
    /// the user to accept — never applied, because a wrong guess would reshuffle their wallet
    /// silently.
    /// </summary>
    public async Task<List<JourneySuggestionResponse>> SuggestAsync(string userId, CancellationToken ct = default)
    {
        var candidates = await _db.ImportedTickets
            .Where(t => t.UserId == userId
                     && t.JourneyId == null
                     && t.Status == ImportedTicketStatus.Active
                     && t.ValidFrom != null)
            .OrderBy(t => t.ValidFrom)
            .Take(200)
            .ToListAsync(ct);

        var suggestions = new List<JourneySuggestionResponse>();
        var used = new HashSet<int>();

        for (var i = 0; i < candidates.Count; i++)
        {
            var seed = candidates[i];
            if (!used.Add(seed.Id)) continue;

            var group = new List<ImportedTicket> { seed };
            var chainEnd = seed.ValidTo ?? seed.ValidFrom!.Value;
            var chainDestination = seed.DestinationName;

            for (var j = i + 1; j < candidates.Count; j++)
            {
                var next = candidates[j];
                if (used.Contains(next.Id)) continue;

                // Two ways a leg joins a trip: it starts close enough in time to the previous leg's
                // end, or it departs from where the previous leg arrived. The second is what makes
                // a multi-day trip group correctly, and it only works because extraction now
                // populates structured origin/destination.
                var closeInTime = next.ValidFrom!.Value - chainEnd <= SuggestionWindow
                               && next.ValidFrom!.Value >= chainEnd.AddHours(-SuggestionWindow.TotalHours);
                var continuesRoute = Connects(chainDestination, next.OriginName);

                if (!closeInTime && !continuesRoute) continue;

                group.Add(next);
                used.Add(next.Id);
                chainEnd = next.ValidTo ?? next.ValidFrom!.Value;
                chainDestination = next.DestinationName ?? chainDestination;
            }

            // A single ticket is not a trip.
            if (group.Count < 2) continue;

            suggestions.Add(new JourneySuggestionResponse
            {
                SuggestedName = NameFor(group),
                Reason = ReasonFor(group),
                Legs = group.Select(ToLeg).ToList(),
                ImportedTicketIds = group.Select(t => t.Id).ToList()
            });
        }

        return suggestions;
    }

    /// <summary>
    /// Rolls journey status forward from its legs. Called by the expiry worker so status cannot
    /// drift from the tickets it describes.
    /// </summary>
    public async Task<int> RollUpStatusesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Only the journeys whose stored status disagrees with their dates are read, and in batches.
        // This used to materialise every non-cancelled journey in the database — every user's, with
        // no paging — on an hourly sweep, only to change the handful whose window had moved.
        //
        // The filter is a predicate rather than an ExecuteUpdate per target status: ExecuteUpdate
        // would push the whole thing into SQL, but the in-memory provider the journey tests run on
        // does not implement it. Narrowing the read gets the same result on both providers, and the
        // decision itself stays in the switch below so there is one definition of it.
        //
        // Cancelled is a user decision and is never rolled over.
        var changed = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var stale = await _db.Journeys
                .Where(j => j.Status != JourneyStatus.Cancelled)
                .Where(j =>
                    // window closed, but not marked Completed
                    (j.StartsAt != null && j.EndsAt != null && j.EndsAt < now && j.Status != JourneyStatus.Completed)
                    // under way, but not marked Active
                    || (j.StartsAt != null && j.StartsAt <= now && (j.EndsAt == null || j.EndsAt >= now)
                        && j.Status != JourneyStatus.Active)
                    // undated or still ahead, but not marked Planned
                    || ((j.StartsAt == null || (j.StartsAt > now && (j.EndsAt == null || j.EndsAt >= now)))
                        && j.Status != JourneyStatus.Planned))
                .OrderBy(j => j.Id)
                .Take(RollUpBatchSize)
                .ToListAsync(ct);

            if (stale.Count == 0) break;

            foreach (var journey in stale)
            {
                var target = journey switch
                {
                    { StartsAt: null } => JourneyStatus.Planned,
                    { EndsAt: not null } when journey.EndsAt < now => JourneyStatus.Completed,
                    _ when journey.StartsAt <= now => JourneyStatus.Active,
                    _ => JourneyStatus.Planned
                };

                if (journey.Status == target) continue;

                journey.Status = target;
                journey.UpdatedAt = now;
                changed++;
            }

            await _db.SaveChangesAsync(ct);

            // A short page means the backlog is drained. Guarding on this rather than looping
            // unconditionally matters because the predicate re-evaluates against rows just written:
            // if one somehow did not converge, an unconditional loop would spin forever.
            if (stale.Count < RollUpBatchSize) break;

            _db.ChangeTracker.Clear();
        }

        return changed;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private async Task<Journey> LoadOwnedAsync(int id, string userId, CancellationToken ct)
    {
        var journey = await _db.Journeys
            .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, ct);

        return journey ?? throw new AppException("Journey not found.", 404);
    }

    /// <summary>
    /// Purchased tickets carry no UserId of their own — ownership runs through Purchase, so every
    /// membership check has to join on it rather than filtering the ticket directly.
    /// </summary>
    private IQueryable<Ticket> OwnedTicketsQuery(string userId) =>
        _db.Tickets.Where(t => _db.Purchases.Any(p => p.Id == t.PurchaseId && p.UserId == userId));

    private async Task AssignAsync(string userId, Journey journey, List<int> importedIds, List<int> ticketIds, CancellationToken ct)
    {
        if (importedIds.Count > 0)
        {
            var imported = await _db.ImportedTickets
                .Where(t => t.UserId == userId && importedIds.Contains(t.Id))
                .ToListAsync(ct);

            // Anything the caller named that we did not find is either someone else's or does not
            // exist; both are the same answer from here, and neither should half-apply.
            if (imported.Count != importedIds.Distinct().Count())
                throw new AppException("One or more of those imported tickets could not be found.", 404);

            foreach (var ticket in imported) ticket.JourneyId = journey.Id;
        }

        if (ticketIds.Count > 0)
        {
            var purchased = await OwnedTicketsQuery(userId)
                .Where(t => ticketIds.Contains(t.Id))
                .ToListAsync(ct);

            if (purchased.Count != ticketIds.Distinct().Count())
                throw new AppException("One or more of those tickets could not be found.", 404);

            foreach (var ticket in purchased) ticket.JourneyId = journey.Id;
        }
    }

    /// <summary>Recomputes the stored window from current membership.</summary>
    private async Task RefreshDatesAsync(Journey journey, CancellationToken ct)
    {
        var importedDates = await _db.ImportedTickets
            .Where(t => t.JourneyId == journey.Id)
            .Select(t => new { t.ValidFrom, t.ValidTo })
            .ToListAsync(ct);

        var ticketDates = await _db.Tickets
            .Where(t => t.JourneyId == journey.Id)
            .Select(t => new { t.ValidFrom, t.ValidTo })
            .ToListAsync(ct);

        var starts = importedDates.Select(d => d.ValidFrom)
            .Concat(ticketDates.Select(d => d.ValidFrom))
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        var ends = importedDates.Select(d => d.ValidTo)
            .Concat(ticketDates.Select(d => d.ValidTo))
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        journey.StartsAt = starts.Count > 0 ? starts.Min() : null;
        journey.EndsAt = ends.Count > 0 ? ends.Max() : null;
    }

    /// <summary>
    /// Whether one leg's arrival is the next leg's departure. Compared loosely because the two
    /// names come from different operators' formatting of the same place — "Zagreb Gl. Kol." and
    /// "Zagreb Glavni Kolodvor" are the same station.
    /// </summary>
    internal static bool Connects(string? destination, string? origin)
    {
        if (string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(origin)) return false;

        var a = Normalise(destination);
        var b = Normalise(origin);
        if (a.Length == 0 || b.Length == 0) return false;

        if (a == b || a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal))
            return true;

        // Fall back to the leading word. Operators abbreviate the qualifier but almost never the
        // place — "Zagreb Gl. Kol." and "Zagreb Glavni Kolodvor" share nothing beyond "Zagreb"
        // once punctuation is stripped, yet they are the same station. Matching on the city is the
        // right granularity here anyway: arriving at one Vienna terminus and departing another
        // still continues the same trip.
        var cityA = LeadingWord(destination);
        var cityB = LeadingWord(origin);

        // Two letters is initials or noise, not a place name.
        return cityA.Length >= 3 && cityA == cityB;
    }

    private static string Normalise(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string LeadingWord(string value)
    {
        var word = value.Trim().Split([' ', ',', '-', '/'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return Normalise(word);
    }

    private static string NameFor(List<ImportedTicket> group)
    {
        var origin = group.Select(t => t.OriginName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        var destination = group.Select(t => t.DestinationName).LastOrDefault(n => !string.IsNullOrWhiteSpace(n));

        if (origin is not null && destination is not null)
            return $"{origin} → {destination}";

        var start = group.Min(t => t.ValidFrom);
        return start.HasValue ? $"Trip on {start.Value:dd MMM yyyy}" : "Trip";
    }

    private static string ReasonFor(List<ImportedTicket> group)
    {
        var chained = group.Zip(group.Skip(1)).Any(pair => Connects(pair.First.DestinationName, pair.Second.OriginName));
        var start = group.Min(t => t.ValidFrom);

        return chained
            ? $"{group.Count} tickets that connect end to end"
            : $"{group.Count} tickets within a day of each other{(start.HasValue ? $" from {start.Value:dd MMM}" : "")}";
    }

    private static JourneyResponse ToResponse(Journey journey, bool includeLegs)
    {
        var response = new JourneyResponse
        {
            Id = journey.Id,
            Name = journey.Name,
            Notes = journey.Notes,
            Status = journey.Status,
            StartsAt = journey.StartsAt,
            EndsAt = journey.EndsAt,
            LegCount = journey.ImportedTickets.Count + journey.Tickets.Count,
            CreatedAt = journey.CreatedAt,
            UpdatedAt = journey.UpdatedAt
        };

        if (!includeLegs) return response;

        response.Legs = journey.ImportedTickets.Select(ToLeg)
            .Concat(journey.Tickets.Select(ToLeg))
            // Legs are a sequence in time; undated ones sort last rather than jumping to the front.
            .OrderBy(l => l.ValidFrom ?? DateTime.MaxValue)
            .ThenBy(l => l.Id)
            .ToList();

        return response;
    }

    private static JourneyLegResponse ToLeg(ImportedTicket t) => new()
    {
        Id = t.Id,
        IsImported = true,
        TicketName = t.TicketName,
        RouteDescription = t.RouteDescription,
        OriginName = t.OriginName,
        DestinationName = t.DestinationName,
        OperatorNameSnapshot = t.OperatorNameSnapshot,
        ValidFrom = t.ValidFrom,
        ValidTo = t.ValidTo,
        Status = t.Status.ToString(),
        Price = t.Price,
        Currency = t.Currency
    };

    private static JourneyLegResponse ToLeg(Ticket t) => new()
    {
        Id = t.Id,
        IsImported = false,
        TicketName = t.ExternalTicketId,
        ValidFrom = t.ValidFrom,
        ValidTo = t.ValidTo,
        Status = t.Status.ToString()
    };
}
