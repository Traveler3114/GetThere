namespace TransitInfoAPI.Entities;

public class CalendarDate
{
    public int Id { get; set; }
    public int FeedVersionId { get; set; }
    public FeedVersion FeedVersion { get; set; } = null!;

    public string ServiceId { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public int ExceptionType { get; set; }
}
