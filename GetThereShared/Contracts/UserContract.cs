namespace GetThereShared.Contracts;

public class ContactResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsFavorite { get; set; }
}

public class SaveContactRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class UserSettingsResponse
{
    public string? Theme { get; set; }
    public string? Language { get; set; }
    public bool NotificationsEnabled { get; set; }
    public string? MapStyle { get; set; }
}

public class UpdateSettingsRequest
{
    public string? Theme { get; set; }
    public string? Language { get; set; }
    public bool? NotificationsEnabled { get; set; }
    public string? MapStyle { get; set; }
}
