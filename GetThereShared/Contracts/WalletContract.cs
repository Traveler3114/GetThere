using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Enums;

namespace GetThereShared.Contracts;

public class WalletResponse
{
    public decimal Balance { get; set; }
    public string Currency { get; set; } = SupportedCurrencies.Default;
    public List<WalletTransactionResponse> RecentTransactions { get; set; } = [];

    [JsonIgnore]
    public string FormattedBalance => MoneyFormatter.Format(Balance, Currency);
}

public class WalletTransactionResponse
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public WalletTransactionType Type { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Currency of this transaction. Set from the wallet it belongs to; falls back to the default so
    /// rows stored before the field existed still render.
    /// </summary>
    public string Currency { get; set; } = SupportedCurrencies.Default;

    [JsonIgnore]
    public string FormattedAmount => MoneyFormatter.Format(Amount, Currency);
}

public record TopUpRequest
{
    [Range(0.01, 1000.0, ErrorMessage = "Amount must be between 0.01 and 1000.")]
    public decimal Amount { get; set; }

    [Required, StringLength(50, MinimumLength = 2)]
    public string PaymentMethod { get; set; } = string.Empty;
}
