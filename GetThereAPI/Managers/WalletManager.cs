using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThereAPI.Managers;

public class WalletManager
{
    private readonly AppDbContext _db;

    public WalletManager(AppDbContext db) { _db = db; }

    public async Task<OperationResult<WalletResponse>> GetWalletAsync(string userId, CancellationToken ct = default)
    {
        var wallet = await _db.Wallets
            .Include(w => w.Transactions.OrderByDescending(t => t.CreatedAt).Take(20))
            .FirstOrDefaultAsync(w => w.UserId == userId, ct);

        if (wallet is null)
            return OperationResult<WalletResponse>.Fail("Wallet not found");

        return OperationResult<WalletResponse>.Ok(new WalletResponse
        {
            Balance = wallet.Balance,
            Currency = wallet.Currency,
            RecentTransactions = wallet.Transactions.Select(t => new WalletTransactionResponse
            {
                Id = t.Id,
                Amount = t.Amount,
                Type = t.Type,
                Description = t.Description,
                CreatedAt = t.CreatedAt
            }).ToList()
        });
    }

    public async Task<OperationResult<WalletResponse>> TopUpAsync(string userId, decimal amount, string paymentMethod, CancellationToken ct = default)
    {
        if (amount <= 0)
            return OperationResult<WalletResponse>.Fail("Amount must be greater than zero.");

        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.UserId == userId, ct);

        if (wallet is null)
            return OperationResult<WalletResponse>.Fail("Wallet not found");

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

        return await GetWalletAsync(userId, ct);
    }

    public async Task<OperationResult<WalletResponse>> EnsureWalletAsync(string userId, CancellationToken ct = default)
    {
        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.UserId == userId, ct);

        if (wallet is null)
        {
            wallet = new Wallet { UserId = userId };
            _db.Wallets.Add(wallet);
            await _db.SaveChangesAsync(ct);
        }

        return await GetWalletAsync(userId, ct);
    }
}
