using System.Text.Json.Serialization;

using GetThereShared.Enums;

namespace GetThereShared.Contracts;

public class WalletResponse
{
    public decimal Balance { get; set; }
    public string Currency { get; set; } = "EUR";
    public List<WalletTransactionResponse> RecentTransactions { get; set; } = [];
}

public class WalletTransactionResponse
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public WalletTransactionType Type { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    [JsonIgnore]
    public string FormattedAmount => $"€{Amount:N2}";
}

public record TopUpRequest
{
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
}
