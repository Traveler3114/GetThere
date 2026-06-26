using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class CustomFeedRun
{
    public int Id { get; set; }
    public int CustomFeedId { get; set; }
    public CustomFeed CustomFeed { get; set; } = null!;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public CustomFeedRunStatus Status { get; set; }
    public int RecordsProduced { get; set; }
    public string LogText { get; set; } = string.Empty;
}
