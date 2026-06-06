using GetThereShared.Enums;

namespace GetThereShared.Contracts;

public class WalletResponse
{
    public int Id { get; set; }
    public decimal Balance { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class WalletTransactionResponse
{
    public int Id { get; set; }
    public WalletTransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Description { get; set; }
    public int WalletId { get; set; }
    public int? TicketId { get; set; }

    public string FormattedAmount => Type == WalletTransactionType.TicketPurchase
        ? $"-€{Amount:F2}"
        : $"+€{Amount:F2}";
}
