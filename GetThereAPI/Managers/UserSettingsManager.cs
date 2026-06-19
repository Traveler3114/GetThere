using Microsoft.EntityFrameworkCore;

using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereShared.Contracts;

namespace GetThereAPI.Managers;

public class UserSettingsManager
{
    private readonly AppDbContext _db;

    public UserSettingsManager(AppDbContext db) { _db = db; }

    public async Task<UserSettingsResponse> GetSettingsAsync(string userId, CancellationToken ct = default)
    {
        var settings = await _db.UserSettings
            .FirstOrDefaultAsync(us => us.UserId == userId, ct);

        if (settings is null)
        {
            settings = new UserSettings { UserId = userId };
            _db.UserSettings.Add(settings);
            await _db.SaveChangesAsync(ct);
        }

        return MapResponse(settings);
    }

    public async Task<UserSettingsResponse> UpdateSettingsAsync(string userId, UpdateSettingsRequest request, CancellationToken ct = default)
    {
        var settings = await _db.UserSettings
            .FirstOrDefaultAsync(us => us.UserId == userId, ct);

        if (settings is null)
        {
            settings = new UserSettings { UserId = userId };
            _db.UserSettings.Add(settings);
        }

        if (request.Theme is not null) settings.Theme = request.Theme;
        if (request.Language is not null) settings.Language = request.Language;
        if (request.NotificationsEnabled.HasValue) settings.NotificationsEnabled = request.NotificationsEnabled.Value;
        if (request.MapStyle is not null) settings.MapStyle = request.MapStyle;

        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return MapResponse(settings);
    }

    private static UserSettingsResponse MapResponse(UserSettings settings) => new()
    {
        Theme = settings.Theme,
        Language = settings.Language,
        NotificationsEnabled = settings.NotificationsEnabled,
        MapStyle = settings.MapStyle
    };
}
