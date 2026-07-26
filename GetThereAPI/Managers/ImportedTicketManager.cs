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
        if (validFrom.HasValue)
            query = query.Where(t => t.ValidFrom >= validFrom.Value);
        if (validTo.HasValue)
            query = query.Where(t => t.ValidTo <= validTo.Value);

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

        if (request.ValidFrom.HasValue && request.ValidTo.HasValue && request.ValidTo <= request.ValidFrom)
            throw new AppException("ValidTo must be after ValidFrom.", 400);

        if (request.Currency is not null && !SupportedCurrencies.All.Contains(request.Currency.ToUpperInvariant()))
            throw new AppException($"Unsupported currency '{request.Currency}'. Supported: {string.Join(", ", SupportedCurrencies.All)}", 400);

        var dedupeHash = ComputeDedupeHash(request);

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
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
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
        if (entity.Status == ImportedTicketStatus.Cancelled)
            throw new AppException("Cannot update a cancelled ticket.", 400);

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

        entity.Status = ImportedTicketStatus.Cancelled;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Imported ticket {Id} cancelled by user {UserId}", id, userId);
    }

    private static bool IsValidTransition(ImportedTicketStatus from, ImportedTicketStatus to) => (from, to) switch
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

    private static string? ComputeDedupeHash(CreateImportedTicketRequest request)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.RawPayload))
            sb.Append(request.RawPayload.Trim());
        else
        {
            sb.Append(request.OperatorGlobalId);
            sb.Append('|');
            sb.Append(request.RouteDescription);
            sb.Append('|');
            sb.Append(request.ValidFrom?.ToString("O"));
            sb.Append('|');
            sb.Append(request.ValidTo?.ToString("O"));
            sb.Append('|');
            sb.Append(request.Source?.ToString());
            sb.Append('|');
            sb.Append(request.TicketName);
        }
        var input = sb.ToString();
        if (string.IsNullOrWhiteSpace(input)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
