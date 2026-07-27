using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereAPI.Mapping;
using GetThereAPI.Models;
using GetThereAPI.Sdk;

using GetThereShared.Contracts;
using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class TicketingManager
{
    private readonly AppDbContext _db;
    private readonly AdapterRegistry _registry;
    private readonly ILogger<TicketingManager> _logger;

    public TicketingManager(AppDbContext db, AdapterRegistry registry, ILogger<TicketingManager> logger)
    {
        _db = db;
        _registry = registry;
        _logger = logger;
    }

    public async Task<List<TicketOptionResponse>> GetTicketOptionsAsync(CancellationToken ct = default)
    {
        var options = await _db.TicketOptions
            .Include(to => to.Adapter)
            .Where(to => to.IsActive)
            .OrderBy(to => to.Price)
            .AsNoTracking()
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
            .AsNoTracking()
            .ToListAsync(ct);

        return tickets.Select(TicketMapper.ToTicketResponse).ToList();
    }

    /// <summary>
    /// Buys a ticket, in three stages so that money and ticket cannot diverge.
    /// <list type="number">
    /// <item>Everything that can fail without touching money is checked first — adapter, option,
    /// currency, idempotency. Nothing is debited until all of it passes.</item>
    /// <item>The debit, its ledger row and a <see cref="PaymentStatus.Pending"/> purchase are
    /// committed together, then the transaction is closed <b>before</b> the adapter is called. No
    /// SQL transaction and no wallet row lock is held across an outbound HTTP request.</item>
    /// <item>On success the ticket is recorded and the purchase completed. On any failure the debit
    /// is reversed with a compensating <see cref="WalletTransactionType.Refund"/> row and the
    /// purchase is marked <see cref="PaymentStatus.Refunded"/>.</item>
    /// </list>
    /// A purchase left <see cref="PaymentStatus.Pending"/> means the process died between stages;
    /// it is recoverable, and the admin console already surfaces the oldest one.
    /// </summary>
    public async Task<TicketResponse> PurchaseTicketAsync(
        string userId, int adapterId, int optionId, string? idempotencyKey = null, CancellationToken ct = default)
    {
        _logger.LogInformation("User {UserId} attempting purchase of option {OptionId} via adapter {AdapterId}", userId, optionId, adapterId);

        // ---- Stage 1: validate everything before any money moves -------------------------------

        var adapter = await _db.TicketingAdapters.FindAsync([adapterId], ct);
        if (adapter is null || !adapter.IsActive)
            throw new AppException("Ticketing adapter not found or inactive.", 404);

        // Resolved up front, not after the debit: with no implementation registered every purchase
        // would otherwise take money and then fail.
        var adapterInstance = _registry.Get(adapter.AdapterType);
        if (adapterInstance is null)
        {
            _logger.LogError("No ITicketingAdapter registered for adapter type {AdapterType}", adapter.AdapterType);
            throw new AppException("This operator's ticketing is temporarily unavailable.", 503, "ADAPTER_NOT_REGISTERED");
        }

        var option = await _db.TicketOptions
            .AsNoTracking()
            .FirstOrDefaultAsync(to => to.Id == optionId && to.TicketingAdapterId == adapterId && to.IsActive, ct);
        if (option is null)
            throw new AppException("Ticket option not found.", 404);

        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct)
            ?? throw new AppException("Wallet not found.", 404, "WALLET_NOT_FOUND");

        // No conversion service exists, so a cross-currency purchase would silently debit the raw
        // numeric amount — 100 USD taking 100 EUR.
        if (!string.Equals(wallet.Currency, option.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException(
                $"This ticket is priced in {option.Currency} but your wallet is in {wallet.Currency}.",
                400, "CURRENCY_MISMATCH");
        }

        if (idempotencyKey is not null)
        {
            var replay = await FindCompletedPurchaseAsync(userId, idempotencyKey, ct);
            if (replay is not null)
            {
                _logger.LogInformation("Replaying idempotent purchase {PurchaseId} for user {UserId}", replay.Value.Id, userId);
                return replay.Value.Ticket;
            }
        }

        // ---- Stage 2: debit, commit, and release the wallet row ---------------------------------

        Purchase purchase;
        WalletTransaction debit;

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            var debitedAt = DateTime.UtcNow;

            // Atomic and conditional: two concurrent purchases cannot both pass the balance check.
            var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Wallets SET Balance = Balance - {option.Price}, UpdatedAt = {debitedAt} WHERE Id = {wallet.Id} AND Balance >= {option.Price}", ct);
            if (rowsAffected == 0)
                throw new AppException("Insufficient balance.", 400, "INSUFFICIENT_BALANCE");

            // Re-read rather than trusting the entity: the raw UPDATE does not refresh the tracker.
            var balanceAfter = await ReadBalanceAsync(wallet.Id, ct);

            debit = new WalletTransaction
            {
                WalletId = wallet.Id,
                Amount = -option.Price,
                BalanceBefore = balanceAfter + option.Price,
                BalanceAfter = balanceAfter,
                Type = WalletTransactionType.TicketPurchase,
                Description = $"Purchase: {option.Name}",
                CreatedAt = debitedAt
            };
            _db.WalletTransactions.Add(debit);
            await _db.SaveChangesAsync(ct);

            purchase = new Purchase
            {
                UserId = userId,
                TicketingAdapterId = adapterId,
                TicketOptionId = optionId,
                WalletTransactionId = debit.Id,
                IdempotencyKey = idempotencyKey,
                Amount = option.Price,
                Currency = option.Currency,
                Status = PaymentStatus.Pending,
                PurchasedAt = debitedAt
            };
            _db.Purchases.Add(purchase);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Another request with the same idempotency key won the race.
                await tx.RollbackAsync(ct);
                _logger.LogInformation("Concurrent purchase with the same idempotency key for user {UserId}", userId);

                var replay = idempotencyKey is null ? null : await FindCompletedPurchaseAsync(userId, idempotencyKey, ct);
                if (replay is not null) return replay.Value.Ticket;

                throw new AppException("A purchase with this key is already in progress.", 409, "DUPLICATE_PURCHASE");
            }

            await tx.CommitAsync(ct);
        }

        // ---- Stage 3: call the adapter with no transaction open --------------------------------

        PurchaseResult purchaseResult;
        try
        {
            purchaseResult = await adapterInstance.PurchaseAsync(new PurchaseRequest
            {
                TicketingAdapterId = adapterId,
                TicketOptionId = optionId,
                UserId = userId
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adapter {AdapterType} threw during purchase {PurchaseId}", adapter.AdapterType, purchase.Id);
            await RefundAsync(purchase, wallet.Id, option.Price, $"Adapter error: {ex.Message}", ct);
            throw new AppException("The ticket could not be issued. Your balance has been restored.", 502, "ADAPTER_FAILED");
        }

        if (!purchaseResult.Success || purchaseResult.Ticket is null)
        {
            var reason = purchaseResult.ErrorMessage ?? "Adapter returned no ticket.";
            _logger.LogWarning("Purchase {PurchaseId} failed for user {UserId}: {Reason}", purchase.Id, userId, reason);
            await RefundAsync(purchase, wallet.Id, option.Price, reason, ct);
            throw new AppException($"{reason} Your balance has been restored.", 400, "PURCHASE_FAILED");
        }

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

        _logger.LogInformation("User {UserId} successfully purchased ticket {TicketId} for option {OptionId}", userId, ticket.Id, optionId);

        // Re-read with the navigations the mapper needs. Mapping the in-memory entity relies on
        // change-tracker fixup to have populated Purchase.TicketOption, which it cannot do here —
        // the option is read AsNoTracking — so the mapper would dereference null.
        return await LoadTicketResponseAsync(ticket.Id, ct);
    }

    /// <summary>Loads a ticket with the purchase, option and adapter that <see cref="TicketMapper"/> reads.</summary>
    private async Task<TicketResponse> LoadTicketResponseAsync(int ticketId, CancellationToken ct)
    {
        var loaded = await _db.Tickets
            .Include(t => t.Purchase).ThenInclude(p => p.TicketOption)
            .Include(t => t.Purchase).ThenInclude(p => p.Adapter)
            .AsNoTracking()
            .FirstAsync(t => t.Id == ticketId, ct);

        return TicketMapper.ToTicketResponse(loaded);
    }

    /// <summary>
    /// Reverses a debit that bought nothing. Written as a credit plus a ledger row rather than by
    /// deleting the debit, so the wallet history shows what happened.
    /// </summary>
    private async Task RefundAsync(Purchase purchase, int walletId, decimal amount, string reason, CancellationToken ct)
    {
        var refundedAt = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Wallets SET Balance = Balance + {amount}, UpdatedAt = {refundedAt} WHERE Id = {walletId}", ct);

        var balanceAfter = await ReadBalanceAsync(walletId, ct);

        _db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = walletId,
            Amount = amount,
            BalanceBefore = balanceAfter - amount,
            BalanceAfter = balanceAfter,
            Type = WalletTransactionType.Refund,
            Description = $"Refund: {reason}",
            ReferenceId = purchase.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CreatedAt = refundedAt
        });

        purchase.Status = PaymentStatus.Refunded;
        purchase.FailureReason = reason;
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        _logger.LogInformation("Refunded {Amount} to wallet {WalletId} for failed purchase {PurchaseId}", amount, walletId, purchase.Id);
    }

    /// <summary>Reads the committed balance straight from the database, bypassing the change tracker.</summary>
    private async Task<decimal> ReadBalanceAsync(int walletId, CancellationToken ct) =>
        await _db.Wallets.AsNoTracking().Where(w => w.Id == walletId).Select(w => w.Balance).FirstAsync(ct);

    /// <summary>Returns the ticket from an earlier purchase with this idempotency key, if it completed.</summary>
    private async Task<(int Id, TicketResponse Ticket)?> FindCompletedPurchaseAsync(
        string userId, string idempotencyKey, CancellationToken ct)
    {
        var existing = await _db.Purchases
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IdempotencyKey == idempotencyKey, ct);

        if (existing is null) return null;

        var ticket = await _db.Tickets
            .Include(t => t.Purchase).ThenInclude(p => p.TicketOption)
            .Include(t => t.Purchase).ThenInclude(p => p.Adapter)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.PurchaseId == existing.Id, ct);

        if (ticket is not null)
            return (existing.Id, TicketMapper.ToTicketResponse(ticket));

        // Key was used but produced no ticket — the original attempt failed and was refunded.
        throw new AppException(
            existing.FailureReason ?? "The original purchase with this key did not succeed.",
            409, "DUPLICATE_PURCHASE");
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
}
