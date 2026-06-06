using GetThereShared.Enums;

namespace GetThereShared.Contracts;

public class PaymentResponse
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus? Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public int WalletId { get; set; }
    public int? PaymentProviderId { get; set; }
    public string? ProviderTransactionId { get; set; }
}

public class PaymentProviderResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public record TopUpRequest
{
    public string UserId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int? PaymentProviderId { get; set; }
}
