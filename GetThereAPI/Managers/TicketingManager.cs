using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Models;
using GetThereAPI.Sdk;
using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThereAPI.Managers;

public class TicketingManager
{
    private readonly AppDbContext _db;
    private readonly AdapterRegistry _registry;
    private readonly WalletManager _walletManager;

    public TicketingManager(AppDbContext db, AdapterRegistry registry, WalletManager walletManager)
    {
        _db = db;
        _registry = registry;
        _walletManager = walletManager;
    }

    public async Task<OperationResult<List<TicketOptionResponse>>> GetTicketOptionsAsync(CancellationToken ct = default)
    {
        var options = await _db.TicketOptions
            .Include(to => to.Adapter)
            .Where(to => to.IsActive)
            .OrderBy(to => to.Price)
            .ToListAsync(ct);

        return OperationResult<List<TicketOptionResponse>>.Ok(
            options.Select(MapOption).ToList());
    }

    public async Task<OperationResult<List<TicketResponse>>> GetUserTicketsAsync(string userId, CancellationToken ct = default)
    {
        var tickets = await _db.Tickets
            .Include(t => t.Purchase)
                .ThenInclude(p => p.TicketOption)
            .Include(t => t.Purchase)
                .ThenInclude(p => p.Adapter)
            .Where(t => t.Purchase.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return OperationResult<List<TicketResponse>>.Ok(
            tickets.Select(MapTicket).ToList());
    }

    public async Task<OperationResult<TicketResponse>> PurchaseTicketAsync(
        string userId, int adapterId, int optionId, CancellationToken ct = default)
    {
        var adapter = await _db.TicketingAdapters.FindAsync(new object[] { adapterId }, ct);
        if (adapter is null || !adapter.IsActive)
            return OperationResult<TicketResponse>.Fail("Ticketing adapter not found or inactive.");

        var option = await _db.TicketOptions
            .FirstOrDefaultAsync(to => to.Id == optionId && to.TicketingAdapterId == adapterId && to.IsActive, ct);
        if (option is null)
            return OperationResult<TicketResponse>.Fail("Ticket option not found.");

        var walletResult = await _walletManager.EnsureWalletAsync(userId, ct);
        if (!walletResult.Success)
            return OperationResult<TicketResponse>.Fail("Could not access wallet.");

        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null || wallet.Balance < option.Price)
            return OperationResult<TicketResponse>.Fail("Insufficient balance.");

        var balanceBefore = wallet.Balance;
        wallet.Balance -= option.Price;
        wallet.UpdatedAt = DateTime.UtcNow;

        var transaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            Amount = -option.Price,
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Type = WalletTransactionType.TicketPurchase,
            Description = $"Purchase: {option.Name}"
        };
        _db.WalletTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);

        var purchase = new Purchase
        {
            UserId = userId,
            TicketingAdapterId = adapterId,
            TicketOptionId = optionId,
            WalletTransactionId = transaction.Id,
            Amount = option.Price,
            Currency = option.Currency,
            Status = PaymentStatus.Pending,
            PurchasedAt = DateTime.UtcNow
        };
        _db.Purchases.Add(purchase);
        await _db.SaveChangesAsync(ct);

        var adapterInstance = _registry.Get(adapter.AdapterType);
        if (adapterInstance is not null)
        {
            var purchaseResult = await adapterInstance.PurchaseAsync(new PurchaseRequest
            {
                TicketingAdapterId = adapterId,
                TicketOptionId = optionId,
                UserId = userId
            }, ct);

            if (purchaseResult.Success && purchaseResult.Ticket is not null)
            {
                purchase.ExternalPurchaseId = purchaseResult.ExternalPurchaseId;
                purchase.Status = PaymentStatus.Completed;
                purchase.CompletedAt = DateTime.UtcNow;

                var ticket = new Ticket
                {
                    PurchaseId = purchase.Id,
                    ExternalTicketId = purchaseResult.ExternalPurchaseId,
                    Format = purchaseResult.Ticket.Format,
                    Data = purchaseResult.Ticket.Data,
                    ValidFrom = purchaseResult.Ticket.ValidFrom,
                    ValidTo = purchaseResult.Ticket.ValidTo,
                    Status = TicketStatus.Active
                };
                _db.Tickets.Add(ticket);
                await _db.SaveChangesAsync(ct);

                return OperationResult<TicketResponse>.Ok(MapTicket(ticket));
            }

            purchase.FailureReason = purchaseResult.ErrorMessage ?? "Adapter returned no ticket.";
            purchase.Status = PaymentStatus.Failed;
            await _db.SaveChangesAsync(ct);

            return OperationResult<TicketResponse>.Fail(purchase.FailureReason);
        }

        purchase.Status = PaymentStatus.Completed;
        purchase.CompletedAt = DateTime.UtcNow;
        purchase.ExternalPurchaseId = $"local-{purchase.Id}";
        await _db.SaveChangesAsync(ct);

        var localTicket = new Ticket
        {
            PurchaseId = purchase.Id,
            Format = option.TicketFormat,
            Data = $"LOCAL:{option.Name}:{purchase.Id}",
            Status = TicketStatus.Active
        };
        _db.Tickets.Add(localTicket);
        await _db.SaveChangesAsync(ct);

        return OperationResult<TicketResponse>.Ok(MapTicket(localTicket));
    }

    private static TicketOptionResponse MapOption(TicketOption to) => new()
    {
        Id = to.Id,
        AdapterId = to.TicketingAdapterId,
        AdapterName = to.Adapter.Name,
        ExternalProductId = to.ExternalProductId,
        Name = to.Name,
        Description = to.Description,
        Price = to.Price,
        Currency = to.Currency,
        TicketFormat = to.TicketFormat,
        DurationMinutes = to.DurationMinutes
    };

    private static TicketResponse MapTicket(Ticket t) => new()
    {
        Id = t.Id,
        PurchaseId = t.PurchaseId,
        ExternalTicketId = t.ExternalTicketId,
        Format = t.Format,
        Data = t.Data,
        ValidFrom = t.ValidFrom,
        ValidTo = t.ValidTo,
        Status = t.Status,
        Option = new TicketOptionResponse
        {
            Id = t.Purchase.TicketOption.Id,
            AdapterId = t.Purchase.TicketingAdapterId,
            AdapterName = t.Purchase.Adapter.Name,
            ExternalProductId = t.Purchase.TicketOption.ExternalProductId,
            Name = t.Purchase.TicketOption.Name,
            Price = t.Purchase.TicketOption.Price,
            Currency = t.Purchase.TicketOption.Currency,
            TicketFormat = t.Purchase.TicketOption.TicketFormat,
            DurationMinutes = t.Purchase.TicketOption.DurationMinutes
        }
    };
}
