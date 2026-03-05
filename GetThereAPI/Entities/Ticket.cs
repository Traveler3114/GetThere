using System.ComponentModel.DataAnnotations.Schema;
using GetThereShared.Enums;

namespace GetThereAPI.Entities;

public class Ticket
{
    public int Id { get; set; }

    // Human-readable name returned by the operator e.g. "Single Journey"
    public string TicketType { get; set; } = string.Empty;

    public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }

    [Column(TypeName = "decimal(16,2)")]
    public decimal PricePaid { get; set; }

    public TicketFormat? Format { get; set; }
    public string? Payload { get; set; }             // QR code string, PDF url, etc.
    public string? DisplayInstructions { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Active;

    // The operator's own ID for this ticket (for validation/refund calls back to them)
    public string? TicketDefinitionId { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    public int TransitOperatorId { get; set; }
    public TransitOperator TransitOperator { get; set; } = null!;
}