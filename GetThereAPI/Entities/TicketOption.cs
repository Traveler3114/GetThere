using GetThereShared.Enums;

namespace GetThereAPI.Entities;

public class TicketOption
{
    public int Id { get; set; }

    public int TicketingAdapterId { get; set; }
    public TicketingAdapter Adapter { get; set; } = null!;

    public string ExternalProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EUR";
    public TicketFormat TicketFormat { get; set; }
    public int? DurationMinutes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
