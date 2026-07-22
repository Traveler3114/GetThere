namespace TransitInfoAPI.Entities;

public class AuditLog
{
    public int Id { get; set; }

    public string? UserId { get; set; }
    public AppUser? User { get; set; }

    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
