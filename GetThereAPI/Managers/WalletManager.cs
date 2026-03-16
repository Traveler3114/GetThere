using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class WalletManager
{
    private readonly AppDbContext _context;

    public WalletManager(AppDbContext context)
    {
        _context = context;
    }

    public async Task CreateWalletForUserAsync(string userId)
    {
        var wallet = new Wallet
        {
            UserId = userId,
            Balance = 0,
            LastUpdated = DateTime.UtcNow
        };
        _context.Wallets.Add(wallet);
        await _context.SaveChangesAsync();
    }

    public async Task<OperationResult<WalletDto>> GetWalletAsync(string userId)
    {
        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet == null)
            return OperationResult<WalletDto>.Fail("Wallet not found.");

        var dto = new WalletDto
        {
            Id = wallet.Id,
            Balance = wallet.Balance,
            LastUpdated = wallet.LastUpdated
        };
        return OperationResult<WalletDto>.Ok(dto);
    }

    public async Task<OperationResult<IEnumerable<WalletTransactionDto>>> GetTransactionsAsync(string userId)
    {
        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet == null)
            return OperationResult<IEnumerable<WalletTransactionDto>>.Fail("Wallet not found.");

        var transactions = await _context.WalletTransactions
            .Where(t => t.WalletId == wallet.Id)
            .OrderByDescending(t => t.Timestamp)
            .Select(t => new WalletTransactionDto
            {
                Id = t.Id,
                Type = t.Type,
                Amount = t.Amount,
                Timestamp = t.Timestamp,
                Description = t.Description,
                WalletId = t.WalletId,
                TicketId = t.TicketId
            })
            .ToListAsync();

        return OperationResult<IEnumerable<WalletTransactionDto>>.Ok(transactions);
    }
}