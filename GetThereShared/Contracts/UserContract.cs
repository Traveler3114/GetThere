namespace GetThereShared.Contracts;

public class UserSettingsResponse
{
    public string? Theme { get; set; }
    public string? Language { get; set; }
    public bool NotificationsEnabled { get; set; }
    public string? MapStyle { get; set; }
}

public record UpdateSettingsRequest
{
    public string? Theme { get; set; }
    public string? Language { get; set; }
    public bool? NotificationsEnabled { get; set; }
    public string? MapStyle { get; set; }
}
