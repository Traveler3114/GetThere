using GetThereShared.Enums;

namespace GetThereAPI.Entities;

public class TicketType
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EUR";
    public TicketFormat TicketFormat { get; set; }
    public int? DurationMinutes { get; set; }
    public int? ValidityDays { get; set; }
    public int? TransferCount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
