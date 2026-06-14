using GetThereShared.Enums;

namespace GetThereAPI.Entities;

public class Ticket
{
    public int Id { get; set; }

    public int PurchaseId { get; set; }
    public Purchase Purchase { get; set; } = null!;

    public string? ExternalTicketId { get; set; }
    public TicketFormat Format { get; set; }
    public string Data { get; set; } = string.Empty;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
