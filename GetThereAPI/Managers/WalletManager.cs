using System.Globalization;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereAPI.Mapping;

using GetThereShared.Contracts;
using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GetThereAPI.Managers;

public class WalletManager
{
    private readonly AppDbContext _db;
    private readonly ILogger<WalletManager> _logger;

    public WalletManager(AppDbContext db, ILogger<WalletManager> logger) { _db = db; _logger = logger; }

    public async Task<WalletResponse?> GetWalletAsync(string userId, CancellationToken ct = default)
    {
        var wallet = await _db.Wallets
            .Include(w => w.Transactions.OrderByDescending(t => t.CreatedAt).Take(20))
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, ct);

        return wallet is null ? null : WalletMapper.ToResponse(wallet);
    }

    /// <summary>Largest single top-up. A bound is the difference between a typo and a fortune.</summary>
    public const decimal MaxTopUpAmount = 1000m;

    /// <summary>
    /// Credits the wallet.
    /// <para>
    /// NOTE: there is still no payment provider behind this — nothing is actually charged. The
    /// endpoint is gated on an admin-only permission for that reason; see
    /// <c>docs/money-path-defects.md</c>. Wiring a provider means taking payment here and only
    /// crediting on a confirmed settlement.
    /// </para>
    /// </summary>
    public async Task<WalletResponse> TopUpAsync(string userId, decimal amount, string paymentMethod, CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new AppException("Amount must be greater than zero.", 400, "INVALID_AMOUNT");

        if (amount > MaxTopUpAmount)
            throw new AppException($"Amount may not exceed {MaxTopUpAmount:N2}.", 400, "AMOUNT_TOO_LARGE");

        // Guards against a fractional-cent amount that would not survive the decimal(18,2) column.
        if (decimal.Round(amount, 2) != amount)
            throw new AppException("Amount may not have more than two decimal places.", 400, "INVALID_AMOUNT");

        if (string.IsNullOrWhiteSpace(paymentMethod))
            throw new AppException("Payment method is required.", 400, "INVALID_PAYMENT_METHOD");

        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct)
            ?? throw new AppException("Wallet not found", 404);

        var creditedAt = DateTime.UtcNow;

        await using var tx = await BeginIfNoneAsync(ct);

        // Atomic UPDATE prevents race conditions on concurrent top-ups.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Wallets SET Balance = Balance + {amount}, UpdatedAt = {creditedAt} WHERE Id = {wallet.Id}", ct);

        // Re-read the committed value: raw SQL does not refresh the tracked entity, so both the
        // ledger row and the response used to report the balance from *before* the top-up.
        var balanceAfter = await _db.Wallets.AsNoTracking()
            .Where(w => w.Id == wallet.Id).Select(w => w.Balance).FirstAsync(ct);

        _db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            Amount = amount,
            BalanceBefore = balanceAfter - amount,
            BalanceAfter = balanceAfter,
            Type = WalletTransactionType.Deposit,
            Description = $"Top-up via {paymentMethod}",
            ReferenceId = null,
            CreatedAt = creditedAt
        });

        _db.Set<AuditLog>().Add(new AuditLog
        {
            UserId = userId,
            Action = "WalletTopUp",
            EntityType = nameof(Wallet),
            EntityId = wallet.Id.ToString(CultureInfo.InvariantCulture),
            // Serialised rather than concatenated: paymentMethod is caller-supplied, and a quote in
            // it produced an audit row containing malformed JSON that nothing could later parse.
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { amount, method = paymentMethod }),
            CreatedAt = creditedAt
        });

        await _db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        _logger.LogInformation("Wallet {WalletId} topped up {Amount} via {Method}, new balance {Balance}", wallet.Id, amount, paymentMethod, balanceAfter);

        // Return freshly-read state rather than the stale tracked entity.
        return await GetWalletAsync(userId, ct) ?? throw new AppException("Wallet not found", 404);
    }

    /// <summary>
    /// Returns the caller's wallet, creating one if the account predates wallet-on-registration.
    /// <para>
    /// <c>AuthManager.RegisterAsync</c> now creates the wallet with the account, so this is a
    /// backstop for existing users rather than the normal path it used to be.
    /// </para>
    /// </summary>
    public async Task<Wallet> EnsureWalletAsync(string userId, CancellationToken ct = default)
    {
        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.UserId == userId, ct);

        if (wallet is null)
        {
            wallet = new Wallet { UserId = userId };
            _db.Wallets.Add(wallet);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Created wallet for user {UserId}", userId);
        }

        return wallet;
    }

    /// <summary>
    /// Holds funds for a booked buy-on-board leg: moves <paramref name="amount"/> from spendable into
    /// <c>Reserved</c> (total <c>Balance</c> unchanged). Fails if available balance
    /// (<c>Balance − Reserved</c>) cannot cover it. Records a <see cref="WalletTransactionType.Hold"/>.
    /// The app never pays the operator — this is a budget guarantee, released on cancel or on obtaining
    /// the ticket on board.
    /// </summary>
    public async Task ReserveAsync(int walletId, decimal amount, string description, string? reference, CancellationToken ct = default)
    {
        if (amount <= 0) return;
        var at = DateTime.UtcNow;

        await using var tx = await BeginIfNoneAsync(ct);

        var rows = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Wallets SET Reserved = Reserved + {amount}, UpdatedAt = {at} WHERE Id = {walletId} AND Balance - Reserved >= {amount}", ct);
        if (rows == 0)
            throw new AppException("Insufficient balance to reserve.", 400, "INSUFFICIENT_BALANCE");

        var balance = await ReadBalanceAsync(walletId, ct);
        _db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = walletId,
            Amount = amount,
            BalanceBefore = balance,
            BalanceAfter = balance,
            Type = WalletTransactionType.Hold,
            Description = description,
            ReferenceId = reference,
            CreatedAt = at,
        });
        await _db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    /// <summary>Releases a hold: returns <paramref name="amount"/> from <c>Reserved</c> to spendable,
    /// clamped so a double release can't drive it negative. Records a <see cref="WalletTransactionType.Release"/>.</summary>
    public async Task ReleaseAsync(int walletId, decimal amount, string description, string? reference, CancellationToken ct = default)
    {
        if (amount <= 0) return;
        var at = DateTime.UtcNow;

        await using var tx = await BeginIfNoneAsync(ct);

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Wallets SET Reserved = CASE WHEN Reserved >= {amount} THEN Reserved - {amount} ELSE 0 END, UpdatedAt = {at} WHERE Id = {walletId}", ct);

        var balance = await ReadBalanceAsync(walletId, ct);
        _db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = walletId,
            Amount = amount,
            BalanceBefore = balance,
            BalanceAfter = balance,
            Type = WalletTransactionType.Release,
            Description = description,
            ReferenceId = reference,
            CreatedAt = at,
        });
        await _db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Starts a transaction only when the caller has not already opened one on this context.
    /// <para>
    /// <c>JourneyBookingManager.BookAsync</c> wraps a whole booking in a transaction and then calls
    /// in here, and EF throws if a second one is started on the same context. Joining the caller's
    /// transaction is also the behaviour that is wanted rather than merely the one that compiles: a
    /// hold placed during a booking that later fails is undone by that rollback, instead of surviving
    /// it and needing a compensating release.
    /// </para>
    /// </summary>
    private async Task<IDbContextTransaction?> BeginIfNoneAsync(CancellationToken ct) =>
        _db.Database.CurrentTransaction is null ? await _db.Database.BeginTransactionAsync(ct) : null;

    private async Task<decimal> ReadBalanceAsync(int walletId, CancellationToken ct) =>
        await _db.Wallets.AsNoTracking().Where(w => w.Id == walletId).Select(w => w.Balance).FirstAsync(ct);
}
