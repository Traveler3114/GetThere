using GetThereShared.Enums;

namespace GetThereShared.Contracts;

public class TicketOptionResponse
{
    public int Id { get; set; }
    public int AdapterId { get; set; }
    public string AdapterName { get; set; } = string.Empty;
    public string ExternalProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EUR";
    public TicketFormat TicketFormat { get; set; }
    public int? DurationMinutes { get; set; }
}

public class TicketResponse
{
    public int Id { get; set; }
    public int PurchaseId { get; set; }
    public string? ExternalTicketId { get; set; }
    public TicketFormat Format { get; set; }
    public string Data { get; set; } = string.Empty;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public TicketStatus Status { get; set; }
    public TicketOptionResponse Option { get; set; } = null!;
}

public record PurchaseTicketRequest
{
    public int AdapterId { get; set; }
    public int OptionId { get; set; }
}
