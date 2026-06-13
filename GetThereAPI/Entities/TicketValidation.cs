namespace GetThereAPI.Entities;

public class TicketValidation
{
    public int Id { get; set; }

    public int TicketInstanceId { get; set; }
    public TicketInstance TicketInstance { get; set; } = null!;

    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
    public string? ValidatedByUserId { get; set; }
    public bool IsValid { get; set; }
    public string? FailureReason { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
