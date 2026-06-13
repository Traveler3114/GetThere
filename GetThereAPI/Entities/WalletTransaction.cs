using GetThereShared.Enums;

namespace GetThereAPI.Entities;

public class WalletTransaction
{
    public int Id { get; set; }

    public int WalletId { get; set; }
    public Wallet Wallet { get; set; } = null!;

    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public WalletTransactionType Type { get; set; }
    public string? Description { get; set; }
    public string? ReferenceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
