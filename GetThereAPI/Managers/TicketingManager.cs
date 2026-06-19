using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereAPI.Mapping;
using GetThereAPI.Models;
using GetThereAPI.Sdk;
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

    public async Task<List<TicketOptionResponse>> GetTicketOptionsAsync(CancellationToken ct = default)
    {
        var options = await _db.TicketOptions
            .Include(to => to.Adapter)
            .Where(to => to.IsActive)
            .OrderBy(to => to.Price)
            .ToListAsync(ct);

        return options.Select(TicketMapper.ToOptionResponse).ToList();
    }

    public async Task<List<TicketResponse>> GetUserTicketsAsync(string userId, CancellationToken ct = default)
    {
        var tickets = await _db.Tickets
            .Include(t => t.Purchase)
                .ThenInclude(p => p.TicketOption)
            .Include(t => t.Purchase)
                .ThenInclude(p => p.Adapter)
            .Where(t => t.Purchase.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return tickets.Select(TicketMapper.ToTicketResponse).ToList();
    }

    public async Task<TicketResponse> PurchaseTicketAsync(
        string userId, int adapterId, int optionId, CancellationToken ct = default)
    {
        var adapter = await _db.TicketingAdapters.FindAsync([adapterId], ct);
        if (adapter is null || !adapter.IsActive)
            throw new AppException("Ticketing adapter not found or inactive.", 404);

        var option = await _db.TicketOptions
            .FirstOrDefaultAsync(to => to.Id == optionId && to.TicketingAdapterId == adapterId && to.IsActive, ct);
        if (option is null)
            throw new AppException("Ticket option not found.", 404);

        var wallet = await _walletManager.EnsureWalletAsync(userId, ct);
        if (wallet is null || wallet.Balance < option.Price)
            throw new AppException("Insufficient balance.", 400);

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

                return TicketMapper.ToTicketResponse(ticket);
            }

            purchase.FailureReason = purchaseResult.ErrorMessage ?? "Adapter returned no ticket.";
            purchase.Status = PaymentStatus.Failed;
            await _db.SaveChangesAsync(ct);

            throw new AppException(purchase.FailureReason, 400);
        }

        purchase.FailureReason = "No ticketing adapter registered for this adapter type.";
        purchase.Status = PaymentStatus.Failed;
        await _db.SaveChangesAsync(ct);

        throw new AppException(purchase.FailureReason, 400);
    }
}
