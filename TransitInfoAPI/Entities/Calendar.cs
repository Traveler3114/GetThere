namespace TransitInfoAPI.Entities;

public class Calendar
{
    public int Id { get; set; }
    public int FeedVersionId { get; set; }
    public FeedVersion FeedVersion { get; set; } = null!;

    public string ServiceId { get; set; } = string.Empty;
    public bool Monday { get; set; }
    public bool Tuesday { get; set; }
    public bool Wednesday { get; set; }
    public bool Thursday { get; set; }
    public bool Friday { get; set; }
    public bool Saturday { get; set; }
    public bool Sunday { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}
