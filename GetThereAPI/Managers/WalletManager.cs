using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Mapping;
using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Managers;

public class WalletManager
{
    private readonly AppDbContext _context;

    public WalletManager(AppDbContext context)
    {
        _context = context;
    }

    public async Task CreateWalletForUserAsync(string userId, CancellationToken ct = default)
    {
        var wallet = new Wallet
        {
            UserId = userId,
            Balance = 0,
            LastUpdated = DateTime.UtcNow
        };
        _context.Wallets.Add(wallet);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<OperationResult<WalletResponse>> GetWalletAsync(string userId, CancellationToken ct = default)
    {
        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
            return OperationResult<WalletResponse>.Fail("Wallet not found.");

        return OperationResult<WalletResponse>.Ok(WalletMapper.ToResponse(wallet));
    }

    public async Task<OperationResult<IEnumerable<WalletTransactionResponse>>> GetTransactionsAsync(string userId, CancellationToken ct = default)
    {
        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
            return OperationResult<IEnumerable<WalletTransactionResponse>>.Fail("Wallet not found.");

        var transactions = await _context.WalletTransactions
            .Where(t => t.WalletId == wallet.Id)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync(ct);

        return OperationResult<IEnumerable<WalletTransactionResponse>>.Ok(
            transactions.Select(WalletMapper.ToResponse).ToList());
    }
}
