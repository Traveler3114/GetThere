using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Common;
using GetThereShared.Contracts;
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


    public async Task<List<PaymentProviderResponse>> GetActiveProvidersAsync(CancellationToken ct = default)
    {
        return await _context.PaymentProviders
            .Where(p => p.IsActive)
            .OrderBy(p => p.Id)
            .Select(p => new PaymentProviderResponse
            {
                Id = p.Id,
                Name = p.Name
            })
            .ToListAsync(ct);
    }

    public async Task<OperationResult<WalletResponse>> TopUpWalletAsync(string userId, TopUpRequest request, CancellationToken ct = default)
    {
        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet == null)
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

        var dto = new WalletResponse
        {
            Id = wallet.Id,
            Balance = wallet.Balance,
            LastUpdated = wallet.LastUpdated
        };
        return OperationResult<WalletResponse>.Ok(dto, "Wallet topped up successfully.");
    }
}
