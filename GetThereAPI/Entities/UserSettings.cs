namespace GetThereAPI.Entities;

public class UserSettings
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    public string? Theme { get; set; }
    public string? Language { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public string? MapStyle { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
