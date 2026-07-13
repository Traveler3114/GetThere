using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereAPI.Mapping;
using GetThereShared.Contracts;
using GetThereShared.Enums;

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

    public async Task<WalletResponse> TopUpAsync(string userId, decimal amount, string paymentMethod, CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new AppException("Amount must be greater than zero.");

        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.UserId == userId, ct);

        if (wallet is null)
            throw new AppException("Wallet not found", 404);

        var balanceBefore = wallet.Balance;

        // Atomic UPDATE prevents race conditions on concurrent top-ups
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Wallets SET Balance = Balance + {amount}, UpdatedAt = {DateTime.UtcNow} WHERE Id = {wallet.Id}", ct);

        var updatedBalance = balanceBefore + amount;

        _db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            Amount = amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = updatedBalance,
            Type = WalletTransactionType.Deposit,
            Description = $"Top-up via {paymentMethod}",
            ReferenceId = null
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Wallet {WalletId} topped up {Amount} via {Method}, new balance {Balance}", wallet.Id, amount, paymentMethod, updatedBalance);
        return WalletMapper.ToResponse(wallet);
    }

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
