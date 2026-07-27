using GetThereShared.Enums;

namespace GetThereAPI.Entities;

public class Purchase
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    public int TicketingAdapterId { get; set; }
    public TicketingAdapter Adapter { get; set; } = null!;

    public int TicketOptionId { get; set; }
    public TicketOption TicketOption { get; set; } = null!;

    public int? WalletTransactionId { get; set; }
    public WalletTransaction? WalletTransaction { get; set; }

    public string? ExternalPurchaseId { get; set; }

    /// <summary>
    /// Client-supplied key that makes a purchase retry safe. Unique per user, so a repeated request
    /// returns the original outcome instead of charging the wallet twice.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
}
