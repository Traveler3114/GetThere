using GetThereShared.Enums;

namespace GetThereAPI.Entities;

public class TicketInstance
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    public int TicketTypeId { get; set; }
    public TicketType TicketType { get; set; } = null!;

    public TicketStatus Status { get; set; } = TicketStatus.Active;
    public DateTime PurchaseDate { get; set; } = DateTime.UtcNow;
    public DateTime? ActivationDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? WalletTransactionId { get; set; }
    public WalletTransaction? WalletTransaction { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
