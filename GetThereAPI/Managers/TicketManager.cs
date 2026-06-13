using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThereAPI.Managers;

public class TicketManager
{
    private readonly AppDbContext _db;
    private readonly WalletManager _walletManager;

    public TicketManager(AppDbContext db, WalletManager walletManager)
    {
        _db = db;
        _walletManager = walletManager;
    }

    public async Task<OperationResult<List<TicketTypeResponse>>> GetTicketTypesAsync(CancellationToken ct = default)
    {
        var types = await _db.TicketTypes
            .Where(tt => tt.IsActive)
            .OrderBy(tt => tt.Price)
            .ToListAsync(ct);

        return OperationResult<List<TicketTypeResponse>>.Ok(
            types.Select(MapTicketType).ToList());
    }

    public async Task<OperationResult<TicketInstanceResponse>> PurchaseTicketAsync(
        string userId, PurchaseTicketRequest request, CancellationToken ct = default)
    {
        var ticketType = await _db.TicketTypes.FindAsync(new object[] { request.TicketTypeId }, ct);
        if (ticketType is null || !ticketType.IsActive)
            return OperationResult<TicketInstanceResponse>.Fail("Ticket type not found or unavailable.");

        var walletResult = await _walletManager.EnsureWalletAsync(userId, ct);
        if (!walletResult.Success || walletResult.Data is null)
            return OperationResult<TicketInstanceResponse>.Fail("Could not access wallet.");

        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
            return OperationResult<TicketInstanceResponse>.Fail("Wallet not found.");

        if (wallet.Balance < ticketType.Price)
            return OperationResult<TicketInstanceResponse>.Fail("Insufficient balance.");

        var balanceBefore = wallet.Balance;
        wallet.Balance -= ticketType.Price;
        wallet.UpdatedAt = DateTime.UtcNow;

        var transaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            Amount = -ticketType.Price,
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Type = WalletTransactionType.TicketPurchase,
            Description = $"Purchase: {ticketType.Name}"
        };
        _db.WalletTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);

        DateTime? expiryDate = ticketType.ValidityDays.HasValue
            ? DateTime.UtcNow.AddDays(ticketType.ValidityDays.Value)
            : ticketType.DurationMinutes.HasValue
                ? DateTime.UtcNow.AddMinutes(ticketType.DurationMinutes.Value)
                : null;

        var ticket = new TicketInstance
        {
            UserId = userId,
            TicketTypeId = ticketType.Id,
            Status = TicketStatus.Active,
            PurchaseDate = DateTime.UtcNow,
            ExpiryDate = expiryDate,
            WalletTransactionId = transaction.Id
        };
        _db.TicketInstances.Add(ticket);
        await _db.SaveChangesAsync(ct);

        return OperationResult<TicketInstanceResponse>.Ok(new TicketInstanceResponse
        {
            Id = ticket.Id,
            TicketType = MapTicketType(ticketType),
            Status = ticket.Status,
            PurchaseDate = ticket.PurchaseDate,
            ActivationDate = ticket.ActivationDate,
            ExpiryDate = ticket.ExpiryDate
        });
    }

    public async Task<OperationResult<List<TicketInstanceResponse>>> GetUserTicketsAsync(string userId, CancellationToken ct = default)
    {
        var tickets = await _db.TicketInstances
            .Include(ti => ti.TicketType)
            .Where(ti => ti.UserId == userId)
            .OrderByDescending(ti => ti.PurchaseDate)
            .ToListAsync(ct);

        return OperationResult<List<TicketInstanceResponse>>.Ok(
            tickets.Select(t => new TicketInstanceResponse
            {
                Id = t.Id,
                TicketType = MapTicketType(t.TicketType),
                Status = t.Status,
                PurchaseDate = t.PurchaseDate,
                ActivationDate = t.ActivationDate,
                ExpiryDate = t.ExpiryDate
            }).ToList());
    }

    public async Task<OperationResult<TicketValidationResponse>> ValidateTicketAsync(
        int ticketInstanceId, string? inspectorUserId, double? lat, double? lon, CancellationToken ct = default)
    {
        var ticket = await _db.TicketInstances
            .Include(ti => ti.TicketType)
            .FirstOrDefaultAsync(ti => ti.Id == ticketInstanceId, ct);

        if (ticket is null)
            return OperationResult<TicketValidationResponse>.Fail("Ticket not found.");

        string? failureReason = null;
        var isValid = ticket.Status switch
        {
            TicketStatus.Active => true,
            TicketStatus.Expired => (failureReason = "Ticket has expired") is null ? false : false,
            TicketStatus.Used => (failureReason = "Ticket has already been used") is null ? false : false,
            TicketStatus.Cancelled => (failureReason = "Ticket was cancelled") is null ? false : false,
            TicketStatus.Refunded => (failureReason = "Ticket was refunded") is null ? false : false,
            _ => (failureReason = "Invalid ticket status") is null ? false : false
        };

        if (ticket.ExpiryDate.HasValue && ticket.ExpiryDate.Value < DateTime.UtcNow)
        {
            ticket.Status = TicketStatus.Expired;
            isValid = false;
            failureReason = "Ticket has expired";
        }

        var validation = new TicketValidation
        {
            TicketInstanceId = ticket.Id,
            ValidatedAt = DateTime.UtcNow,
            ValidatedByUserId = inspectorUserId,
            IsValid = isValid,
            FailureReason = failureReason,
            Latitude = lat,
            Longitude = lon
        };
        _db.TicketValidations.Add(validation);
        await _db.SaveChangesAsync(ct);

        return OperationResult<TicketValidationResponse>.Ok(new TicketValidationResponse
        {
            Id = validation.Id,
            TicketIdentifier = $"TICKET-{ticket.Id}",
            ValidatedAt = validation.ValidatedAt,
            IsValid = validation.IsValid,
            FailureReason = validation.FailureReason
        });
    }

    private static TicketTypeResponse MapTicketType(TicketType tt) => new()
    {
        Id = tt.Id,
        Name = tt.Name,
        Description = tt.Description,
        Price = tt.Price,
        Currency = tt.Currency,
        TicketFormat = tt.TicketFormat,
        DurationMinutes = tt.DurationMinutes,
        ValidityDays = tt.ValidityDays,
        TransferCount = tt.TransferCount,
        IsActive = tt.IsActive
    };
}
