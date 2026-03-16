using GetThereShared.Enums;

namespace GetThereShared.Dtos;

/// <summary>
/// A payment transaction — used when topping up the wallet
/// or purchasing through a payment provider.
/// </summary>
public class PaymentDto
{
    public int            Id                    { get; set; }
    public decimal        Amount                { get; set; }
    public PaymentStatus? Status                { get; set; }
    public DateTime       CreatedAt             { get; set; }
    public int            WalletId              { get; set; }
    public int?           PaymentProviderId     { get; set; }

    /// <summary>Transaction ID from the external provider (Stripe, PayPal etc).</summary>
    public string?        ProviderTransactionId { get; set; }
}

/// <summary>
/// A payment provider available for topping up (e.g. Stripe, PayPal, bank transfer).
/// </summary>
public class PaymentProviderDto
{
    public int    Id   { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Request to top up the user's wallet.</summary>
public class TopUpDto
{
    public string  UserId            { get; set; } = string.Empty;
    public decimal Amount            { get; set; }
    public int?    PaymentProviderId { get; set; }
}
