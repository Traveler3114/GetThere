namespace TransitInfoAPI.Entities;

public class FeedConverter
{
    public int Id { get; set; }
    public string ConverterType { get; set; } = string.Empty;
    public string? ConverterConfig { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastRun { get; set; }
    public bool LastSuccess { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int FeedId { get; set; }
    public Feed Feed { get; set; } = null!;
}
