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

    /// <summary>
    /// The trip this ticket belongs to. Purchased tickets group into journeys alongside imported
    /// ones — a trip mixes both. Note this entity carries no UserId of its own: ownership runs
    /// through <see cref="Purchase"/>, so membership checks must join on it.
    /// </summary>
    public int? JourneyId { get; set; }
    public Journey? Journey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
