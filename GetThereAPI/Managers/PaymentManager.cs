using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Dtos;
using GetThereShared.Enums;
using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

public class PaymentManager
{
    private readonly AppDbContext _context;

    public PaymentManager(AppDbContext context)
    {
        _context = context;
    }

    public async Task<OperationResult<WalletDto>> TopUpWalletAsync(string userId, TopUpDto request)
    {
        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet == null)
            return OperationResult<WalletDto>.Fail("Wallet not found.");

        if (request.Amount <= 0)
            return OperationResult<WalletDto>.Fail("Amount must be greater than zero.");

        var payment = new Payment
        {
            ProviderTransactionId = Guid.NewGuid().ToString(),
            Amount = request.Amount,
            Status = PaymentStatus.Completed,
            CreatedAt = DateTime.UtcNow,
            WalletId = wallet.Id,
            PaymentProviderId = request.PaymentProviderId
        };

        _context.Payments.Add(payment);

        wallet.Balance += request.Amount;
        wallet.LastUpdated = DateTime.UtcNow;

        _context.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            Type = WalletTransactionType.TopUp,
            Amount = request.Amount,
            Timestamp = DateTime.UtcNow,
            Description = "Manual top-up"
        });

        await _context.SaveChangesAsync();

        var dto = new WalletDto
        {
            Id = wallet.Id,
            Balance = wallet.Balance,
            LastUpdated = wallet.LastUpdated
        };
        return OperationResult<WalletDto>.Ok(dto, "Wallet topped up successfully.");
    }
}