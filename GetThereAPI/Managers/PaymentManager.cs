using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Mapping;
using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThereAPI.Managers;

public class PaymentManager
{
    private readonly AppDbContext _context;

    public PaymentManager(AppDbContext context)
    {
        _context = context;
    }


    public async Task<List<PaymentProviderResponse>> GetActiveProvidersAsync(CancellationToken ct = default)
    {
        var providers = await _context.PaymentProviders
            .Where(p => p.IsActive)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        return providers.Select(PaymentMapper.ToResponse).ToList();
    }

    public async Task<OperationResult<WalletResponse>> TopUpWalletAsync(string userId, TopUpRequest request, CancellationToken ct = default)
    {
        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
        {
            wallet = new Wallet
            {
                UserId = userId,
                Balance = 0m,
                LastUpdated = DateTime.UtcNow
            };

            _context.Wallets.Add(wallet);
            await _context.SaveChangesAsync(ct);
        }

        if (request.Amount <= 0)
            return OperationResult<WalletResponse>.Fail("Amount must be greater than zero.");

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

        await _context.SaveChangesAsync(ct);

        return OperationResult<WalletResponse>.Ok(WalletMapper.ToResponse(wallet), "Wallet topped up successfully.");
    }
}
