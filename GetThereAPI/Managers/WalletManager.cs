using System.Globalization;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereAPI.Mapping;

using GetThereShared.Contracts;
using GetThereShared.Enums;

using Microsoft.EntityFrameworkCore;

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

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

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
        await tx.CommitAsync(ct);

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
}
