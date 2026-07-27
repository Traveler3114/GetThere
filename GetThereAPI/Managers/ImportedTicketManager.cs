using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereAPI.Mapping;

using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class ImportedTicketManager
{
    private readonly AppDbContext _db;
    private readonly ILogger<ImportedTicketManager> _logger;

    public ImportedTicketManager(AppDbContext db, ILogger<ImportedTicketManager> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<ImportedTicketResponse>> ListAsync(string userId, int page, int perPage, ImportedTicketStatus? status = null, ImportSource? source = null, string? operatorId = null, DateTime? validFrom = null, DateTime? validTo = null, string? sort = null, CancellationToken ct = default)
    {
        var query = _db.ImportedTickets.Where(t => t.UserId == userId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);
        if (source.HasValue)
            query = query.Where(t => t.Source == source.Value);
        if (!string.IsNullOrWhiteSpace(operatorId))
            query = query.Where(t => t.OperatorGlobalId == operatorId);
        // Overlap, not containment. Filtering on ValidFrom >= from && ValidTo <= to dropped every
        // ticket that spanned the requested window — exactly the ones a "valid this week" query
        // is looking for. A ticket matches if its validity period intersects the range at all.
        if (validFrom.HasValue)
            query = query.Where(t => t.ValidTo == null || t.ValidTo >= validFrom.Value);
        if (validTo.HasValue)
            query = query.Where(t => t.ValidFrom == null || t.ValidFrom <= validTo.Value);

        query = sort?.ToLowerInvariant() switch
        {
            "createdat" => query.OrderBy(t => t.CreatedAt),
            "-createdat" => query.OrderByDescending(t => t.CreatedAt),
            "validfrom" => query.OrderBy(t => t.ValidFrom),
            "-validfrom" => query.OrderByDescending(t => t.ValidFrom),
            "validto" => query.OrderBy(t => t.ValidTo),
            "-validto" => query.OrderByDescending(t => t.ValidTo),
            "ticketname" => query.OrderBy(t => t.TicketName),
            "-ticketname" => query.OrderByDescending(t => t.TicketName),
            _ => query.OrderByDescending(t => t.CreatedAt)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(ImportedTicketMapper.ToResponseExpression)
            .ToListAsync(ct);

        return new PagedResult<ImportedTicketResponse>(items, total, page, perPage);
    }

    public async Task<ImportedTicketResponse?> GetByIdAsync(int id, string userId, CancellationToken ct = default)
    {
        var ticket = await _db.ImportedTickets
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        return ticket is null ? null : ImportedTicketMapper.ToResponse(ticket);
    }

    public async Task<ImportedTicketResponse> CreateAsync(string userId, CreateImportedTicketRequest request, CancellationToken ct = default)
    {
        if (request.Source is null)
            throw new AppException("Source is required.", 400);

        // Photo/Pdf/QrScan describe a file the caller cannot supply yet — there is no upload
        // endpoint. Accepting them would write tickets claiming a provenance they do not have, and
        // because Source feeds the dedupe hash, the same ticket sent under two sources would evade
        // dedupe entirely. The upload endpoint lifts this guard.
        if (request.Source.Value != ImportSource.Manual)
            throw new AppException($"Import source '{request.Source.Value}' requires a file upload, which is not available yet. Use manual entry.", 400);

        var validFrom = ToUtc(request.ValidFrom);
        var validTo = ToUtc(request.ValidTo);

        if (validFrom.HasValue && validTo.HasValue && validTo <= validFrom)
            throw new AppException("ValidTo must be after ValidFrom.", 400);

        if (request.Currency is not null && !SupportedCurrencies.All.Contains(request.Currency.ToUpperInvariant()))
            throw new AppException($"Unsupported currency '{request.Currency}'. Supported: {string.Join(", ", SupportedCurrencies.All)}", 400);

        // A price without a currency renders against whatever the UI happens to default to, and a
        // currency without a price is meaningless. The client already pairs them; the API has to
        // as well, because it is not the only possible caller.
        if (request.Price.HasValue && request.Currency is null)
            throw new AppException("Currency is required when a price is supplied.", 400);
        if (request.Currency is not null && !request.Price.HasValue)
            throw new AppException("Price is required when a currency is supplied.", 400);

        var dedupeHash = ComputeDedupeHash(request, validFrom, validTo);

        if (dedupeHash is not null)
        {
            var duplicate = await _db.ImportedTickets
                .AnyAsync(t => t.UserId == userId && t.DedupeHash == dedupeHash && t.Status == ImportedTicketStatus.Active, ct);
            if (duplicate)
                throw new AppException("This ticket appears to be a duplicate of an existing active ticket.", 409);
        }

        var entity = new ImportedTicket
        {
            UserId = userId,
            OperatorGlobalId = request.OperatorGlobalId,
            OperatorNameSnapshot = request.OperatorNameSnapshot,
            Source = request.Source.Value,
            Status = ImportedTicketStatus.Active,
            Verification = VerificationStatus.Unverified,
            TicketName = request.TicketName,
            RouteDescription = request.RouteDescription,
            Price = request.Price,
            Currency = request.Currency?.ToUpperInvariant(),
            ValidFrom = validFrom,
            ValidTo = validTo,
            RawPayload = request.RawPayload,
            PayloadFormat = request.PayloadFormat,
            DedupeHash = dedupeHash,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ImportedTickets.Add(entity);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyViolation(ex))
        {
            throw new AppException("This ticket appears to be a duplicate of an existing active ticket.", 409);
        }

        _logger.LogInformation("Imported ticket {Id} created for user {UserId} via {Source}", entity.Id, userId, request.Source);
        return ImportedTicketMapper.ToResponse(entity);
    }

    public async Task<ImportedTicketResponse> UpdateStatusAsync(int id, string userId, ImportedTicketStatus newStatus, CancellationToken ct = default)
    {
        var entity = await _db.ImportedTickets
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        if (entity is null)
            throw new AppException("Imported ticket not found.", 404);

        if (!IsValidTransition(entity.Status, newStatus))
            throw new AppException($"Cannot transition from {entity.Status} to {newStatus}.", 400);

        entity.Status = newStatus;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ImportedTicketMapper.ToResponse(entity);
    }

    public async Task CancelAsync(int id, string userId, CancellationToken ct = default)
    {
        var entity = await _db.ImportedTickets
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        if (entity is null)
            throw new AppException("Imported ticket not found.", 404);

        // Already cancelled is the caller's desired end state, so this succeeds without a write —
        // DELETE should be idempotent, and re-cancelling used to bump UpdatedAt for nothing.
        if (entity.Status == ImportedTicketStatus.Cancelled)
            return;

        // This used to assign Cancelled unconditionally, so DELETE quietly did what
        // PATCH /{id}/status forbids: cancelling a ticket that was already Used or Expired.
        if (!IsValidTransition(entity.Status, ImportedTicketStatus.Cancelled))
            throw new AppException($"Cannot cancel a ticket that is {entity.Status}.", 400);

        entity.Status = ImportedTicketStatus.Cancelled;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Imported ticket {Id} cancelled by user {UserId}", id, userId);
    }

    internal static bool IsValidTransition(ImportedTicketStatus from, ImportedTicketStatus to) => (from, to) switch
    {
        (ImportedTicketStatus.Active, ImportedTicketStatus.Used) => true,
        (ImportedTicketStatus.Active, ImportedTicketStatus.Expired) => true,
        (ImportedTicketStatus.Active, ImportedTicketStatus.Cancelled) => true,
        _ => false
    };

    private static bool IsDuplicateKeyViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
            return sqlEx.Number is 2601 or 2627;
        return false;
    }

    /// <summary>
    /// Forces a wall-clock value to UTC. <see cref="Services.TicketExpiryWorker"/> compares
    /// <c>ValidTo</c> against <see cref="DateTime.UtcNow"/>, but <c>System.Text.Json</c> yields
    /// <see cref="DateTimeKind.Unspecified"/> for an offset-less timestamp and <c>datetime2</c>
    /// carries no kind — so an offset-bearing or naive payload would otherwise be stored verbatim
    /// and expire off by the caller's UTC offset. Unspecified is taken at face value as UTC, which
    /// is what the existing MAUI client already sends.
    /// </summary>
    internal static DateTime? ToUtc(DateTime? value) => value?.Kind switch
    {
        null => null,
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
    };

    /// <summary>
    /// Dates are the already-normalised UTC values rather than the raw request, because the hash
    /// formats them with "O" — hashing before normalising would give the same logical ticket two
    /// different hashes depending on how the caller expressed the offset.
    /// </summary>
    internal static string? ComputeDedupeHash(CreateImportedTicketRequest request, DateTime? validFrom, DateTime? validTo)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.RawPayload))
            sb.Append(request.RawPayload.Trim());
        else
        {
            // Length-prefixed rather than delimiter-joined: a bare '|' separator let a value
            // containing the delimiter shift the field boundary, so TicketName "Zagreb|Rijeka"
            // could collide with a different ticket whose route happened to start "Rijeka".
            AppendField(sb, request.OperatorGlobalId);
            AppendField(sb, request.RouteDescription);
            AppendField(sb, validFrom?.ToString("O"));
            AppendField(sb, validTo?.ToString("O"));
            AppendField(sb, request.Source?.ToString());
            AppendField(sb, request.TicketName);
            // Price is part of the identity: two otherwise-identical tickets bought at different
            // fares are different tickets, and were previously collapsed into one.
            AppendField(sb, request.Price?.ToString(CultureInfo.InvariantCulture));
            AppendField(sb, request.Currency);
        }
        var input = sb.ToString();
        if (string.IsNullOrWhiteSpace(input)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    private static void AppendField(StringBuilder sb, string? value)
    {
        sb.Append(value?.Length ?? -1);
        sb.Append(':');
        sb.Append(value);
        sb.Append('|');
    }
}
