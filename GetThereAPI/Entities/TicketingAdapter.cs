namespace GetThereAPI.Entities;

public class TicketingAdapter
{
    public int Id { get; set; }

    public string TransitInfoGlobalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AdapterType { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? ApiKeyEncrypted { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TicketOption> TicketOptions { get; set; } = [];
}
