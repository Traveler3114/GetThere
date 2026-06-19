using GetThereAPI.Entities;
using GetThereShared.Contracts;

namespace GetThereAPI.Mapping;

public static class WalletMapper
{
    public static WalletResponse ToResponse(Wallet wallet) => new()
    {
        Balance = wallet.Balance,
        Currency = wallet.Currency,
        RecentTransactions = wallet.Transactions
            .OrderByDescending(t => t.CreatedAt)
            .Take(20)
            .Select(t => new WalletTransactionResponse
            {
                Id = t.Id,
                Amount = t.Amount,
                Type = t.Type,
                Description = t.Description,
                CreatedAt = t.CreatedAt
            }).ToList()
    };
}
