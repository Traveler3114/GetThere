using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class MobilityProvider
{
    public int Id { get; set; }
    public FeedFormat FeedFormat { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? ApiKey { get; set; }
    public string? ConverterConfig { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;
}
