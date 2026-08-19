namespace GetThereAPI.Entities;

public class Wallet
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    public decimal Balance { get; set; }

    /// <summary>
    /// Funds held for booked buy-on-board journey legs — inside <see cref="Balance"/> but not
    /// spendable. Available balance is <c>Balance − Reserved</c>; a debit checks against that, and a
    /// journey release returns funds here to spendable. Never fronts money to an operator.
    /// </summary>
    public decimal Reserved { get; set; }

    public string Currency { get; set; } = "EUR";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<WalletTransaction> Transactions { get; set; } = [];
}
