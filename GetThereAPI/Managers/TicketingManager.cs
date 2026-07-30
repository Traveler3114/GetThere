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
                UserId = userId,
                // Our own handle on this attempt. If the answer never comes back, this is what
                // ReconcilePendingPurchasesAsync asks the operator about later.
                PaymentReference = purchase.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
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

        var ticket = await CompletePurchaseAsync(purchase, purchaseResult, ct);

        _logger.LogInformation("User {UserId} successfully purchased ticket {TicketId} for option {OptionId}", userId, ticket.Id, optionId);

        // Re-read with the navigations the mapper needs. Mapping the in-memory entity relies on
        // change-tracker fixup to have populated Purchase.TicketOption, which it cannot do here —
        // the option is read AsNoTracking — so the mapper would dereference null.
        return await LoadTicketResponseAsync(ticket.Id, ct);
    }

    /// <summary>
    /// Records the ticket an operator issued and closes the purchase out. Shared by the live
    /// purchase path and by <see cref="ReconcilePendingPurchasesAsync"/>, so a purchase recovered
    /// hours later ends up in exactly the same state as one that completed inline.
    /// </summary>
    private async Task<Ticket> CompletePurchaseAsync(Purchase purchase, PurchaseResult result, CancellationToken ct)
    {
        purchase.ExternalPurchaseId = result.ExternalPurchaseId;
        purchase.Status = PaymentStatus.Completed;
        purchase.CompletedAt = DateTime.UtcNow;

        var ticket = new Ticket
        {
            PurchaseId = purchase.Id,
            ExternalTicketId = result.ExternalPurchaseId,
            Format = result.Ticket!.Format,
            Data = result.Ticket.Data,
            ValidFrom = result.Ticket.ValidFrom,
            ValidTo = result.Ticket.ValidTo,
            Status = TicketStatus.Active
        };
        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);

        return ticket;
    }

    /// <summary>
    /// Finishes purchases that were debited but never resolved, and returns how many were settled.
    /// <para>
    /// <see cref="PurchaseTicketAsync"/> commits the wallet debit before calling the operator, so a
    /// crash, a pod eviction or a deploy in that window leaves a <see cref="PaymentStatus.Pending"/>
    /// purchase: the user's money is gone and no ticket exists. Nothing used to resolve those — the
    /// row sat there until a human noticed it in the admin console.
    /// </para>
    /// <para>
    /// Each stale purchase is put back to the operator through
    /// <see cref="ITicketingAdapter.FindPurchaseAsync"/>. A ticket the operator did issue is
    /// recorded and the purchase completes; a purchase the operator never saw is refunded. If the
    /// operator cannot be asked — unreachable, or no implementation registered — the purchase is
    /// left exactly as it is and logged, because refunding a ticket that was in fact issued is a
    /// worse outcome than waiting for the next sweep.
    /// </para>
    /// </summary>
    /// <param name="minimumAge">
    /// How long a purchase must have been pending before it is considered stranded. This must stay
    /// comfortably longer than a normal adapter call, or the sweep will race a purchase that is
    /// still legitimately in flight and refund it out from under the caller.
    /// </param>
    /// <summary>
    /// Most stranded purchases the sweep will ever see in one tick. Each one costs an outbound call
    /// to an operator, so the ceiling bounds both the memory and the time a single sweep can take.
    /// </summary>
    private const int ReconcileBatchSize = 200;

    public async Task<int> ReconcilePendingPurchasesAsync(TimeSpan minimumAge, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - minimumAge;

        // Bounded. This read every pending row in the table, and a backlog only grows when something
        // is already wrong — which is exactly when this runs. The remainder is picked up by the next
        // sweep rather than held in memory now.
        var stranded = await _db.Purchases
            .Include(p => p.Adapter)
            .Where(p => p.Status == PaymentStatus.Pending && p.PurchasedAt < cutoff)
            .OrderBy(p => p.PurchasedAt)
            .Take(ReconcileBatchSize)
            .ToListAsync(ct);

        if (stranded.Count == 0) return 0;

        _logger.LogWarning("Found {Count} purchase(s) pending since before {Cutoff}", stranded.Count, cutoff);

        var settled = 0;

        foreach (var purchase in stranded)
        {
            ct.ThrowIfCancellationRequested();

            var wallet = await _db.Wallets.AsNoTracking()
                .FirstOrDefaultAsync(w => w.UserId == purchase.UserId, ct);

            if (wallet is null)
            {
                // Nothing to refund into and nothing sensible to do. Loud, because a purchase whose
                // wallet vanished is a data-integrity problem, not a transient failure.
                _logger.LogError("Pending purchase {PurchaseId} has no wallet for user {UserId}", purchase.Id, purchase.UserId);
                continue;
            }

            var adapterInstance = _registry.Get(purchase.Adapter.AdapterType);
            if (adapterInstance is null)
            {
                _logger.LogError(
                    "Cannot reconcile purchase {PurchaseId}: no ITicketingAdapter registered for {AdapterType}. " +
                    "The debit stands until one is registered.",
                    purchase.Id, purchase.Adapter.AdapterType);
                continue;
            }

            var reference = purchase.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

            PurchaseResult? outcome;
            try
            {
                outcome = await adapterInstance.FindPurchaseAsync(reference, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Adapter {AdapterType} could not be asked about purchase {PurchaseId}; leaving it pending",
                    purchase.Adapter.AdapterType, purchase.Id);
                continue;
            }

            if (outcome is { Success: true, Ticket: not null })
            {
                await CompletePurchaseAsync(purchase, outcome, ct);
                _logger.LogInformation("Recovered purchase {PurchaseId}: the operator had issued a ticket after all", purchase.Id);
            }
            else
            {
                var reason = outcome?.ErrorMessage ?? "No ticket was issued for this purchase.";
                await RefundAsync(purchase, wallet.Id, purchase.Amount, reason, ct);
                _logger.LogInformation("Refunded stranded purchase {PurchaseId}: {Reason}", purchase.Id, reason);
            }

            settled++;
        }

        return settled;
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
        var reference = purchase.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // A refund can be attempted more than once for the same purchase: the live path may fail
        // partway and leave the purchase Pending, and the reconciliation sweep then picks it up.
        // Without a guard the second attempt credits the wallet again and hands out money that was
        // never taken.
        //
        // The guard has to be atomic with the credit, not a read taken before it. It used to be an
        // AnyAsync outside the transaction below, which two writers could both pass before either
        // inserted — and the two writers are a designed-in pair (the live path and the sweep), not a
        // hypothetical.
        //
        // The durable fix is a filtered unique index on (Type, ReferenceId), which needs a migration.
        // Until that exists, the check is moved inside the transaction and made lock-taking:
        // UPDLOCK + HOLDLOCK takes a range lock on the key being tested, so a second writer asking
        // the same question blocks here until the first commits, and then sees the row it wrote.
        // That is the part an EF `AnyAsync` cannot express, which is why it is raw SQL.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var refundMarker = WalletTransactionType.Refund.ToString();
        var existingRefunds = await _db.Database
            .SqlQuery<int>($"""
                SELECT TOP 1 1 AS Value FROM WalletTransactions WITH (UPDLOCK, HOLDLOCK)
                WHERE Type = {refundMarker} AND ReferenceId = {reference}
                """)
            .ToListAsync(ct);

        if (existingRefunds.Count > 0)
        {
            await tx.RollbackAsync(ct);
            _logger.LogInformation("Purchase {PurchaseId} was already refunded; skipping duplicate credit", purchase.Id);

            if (purchase.Status != PaymentStatus.Refunded)
            {
                purchase.Status = PaymentStatus.Refunded;
                purchase.FailureReason ??= reason;
                await _db.SaveChangesAsync(ct);
            }

            return;
        }

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
            ReferenceId = reference,
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
