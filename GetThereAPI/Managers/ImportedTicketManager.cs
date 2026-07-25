using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereShared.Contracts;
using GetThereShared.Enums;

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

    public async Task<List<ImportedTicketResponse>> ListAsync(string userId, ImportedTicketStatus? status = null, ImportSource? source = null, CancellationToken ct = default)
    {
        var query = _db.ImportedTickets.Where(t => t.UserId == userId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);
        if (source.HasValue)
            query = query.Where(t => t.Source == source.Value);

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => ToResponse(t))
            .ToListAsync(ct);
    }

    public async Task<ImportedTicketResponse?> GetByIdAsync(int id, string userId, CancellationToken ct = default)
    {
        var ticket = await _db.ImportedTickets
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);

        return ticket is null ? null : ToResponse(ticket);
    }

    public async Task<ImportedTicketResponse> CreateAsync(string userId, CreateImportedTicketRequest request, CancellationToken ct = default)
    {
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
            Source = request.Source,
            Status = ImportedTicketStatus.Active,
            Verification = VerificationStatus.Unverified,
            TicketName = request.TicketName,
            RouteDescription = request.RouteDescription,
            Price = request.Price,
            Currency = request.Currency,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            RawPayload = request.RawPayload,
            PayloadFormat = request.PayloadFormat,
            SourceFileBlobKey = request.SourceFileBlobKey,
            SourceFileContentType = request.SourceFileContentType,
            DedupeHash = dedupeHash,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ImportedTickets.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Imported ticket {Id} created for user {UserId} via {Source}", entity.Id, userId, request.Source);

        return ToResponse(entity);
    }

    public async Task<ImportedTicketResponse> UpdateStatusAsync(int id, string userId, ImportedTicketStatus newStatus, CancellationToken ct = default)
    {
        var entity = await _db.ImportedTickets
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);

        if (entity is null)
            throw new AppException("Imported ticket not found.", 404);

        if (entity.Status == ImportedTicketStatus.Cancelled)
            throw new AppException("Cannot update a cancelled ticket.", 400);

        entity.Status = newStatus;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToResponse(entity);
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
        }

        var input = sb.ToString();
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    private static ImportedTicketResponse ToResponse(ImportedTicket t) => new()
    {
        Id = t.Id,
        OperatorGlobalId = t.OperatorGlobalId,
        OperatorNameSnapshot = t.OperatorNameSnapshot,
        Source = t.Source,
        Status = t.Status,
        Verification = t.Verification,
        TicketName = t.TicketName,
        RouteDescription = t.RouteDescription,
        Price = t.Price,
        Currency = t.Currency,
        ValidFrom = t.ValidFrom,
        ValidTo = t.ValidTo,
        RawPayload = t.RawPayload,
        PayloadFormat = t.PayloadFormat,
        SourceFileBlobKey = t.SourceFileBlobKey,
        SourceFileContentType = t.SourceFileContentType,
        JourneyId = t.JourneyId,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };
}
