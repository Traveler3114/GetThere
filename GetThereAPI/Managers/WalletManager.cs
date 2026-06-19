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

    public WalletManager(AppDbContext db) { _db = db; }

    public async Task<WalletResponse?> GetWalletAsync(string userId, CancellationToken ct = default)
    {
        var wallet = await _db.Wallets
            .Include(w => w.Transactions.OrderByDescending(t => t.CreatedAt).Take(20))
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
        wallet.Balance += amount;
        wallet.UpdatedAt = DateTime.UtcNow;

        _db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            Amount = amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Type = WalletTransactionType.Deposit,
            Description = $"Top-up via {paymentMethod}",
            ReferenceId = null
        });

        await _db.SaveChangesAsync(ct);

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
        }

        return wallet;
    }
}
