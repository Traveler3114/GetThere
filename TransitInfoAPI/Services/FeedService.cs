using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Common;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Models;

namespace TransitInfoAPI.Services;

public class FeedService
{
    private readonly TransitDbContext _db;
    private readonly FeedImportService _feedImport;

    public FeedService(TransitDbContext db, FeedImportService feedImport)
    {
        _db = db;
        _feedImport = feedImport;
    }

    public async Task<List<FeedDto>> GetAllAsync(CancellationToken ct)
    {
        return await _db.Feeds
            .Include(f => f.Operator)
            .OrderBy(f => f.CreatedAt)
            .Select(f => new FeedDto
            {
                Id = f.Id,
                FeedType = f.FeedType.ToString(),
                FeedId = f.FeedId,
                ExternalUrl = f.ExternalUrl,
                InternalUrl = f.InternalUrl,
                IsActive = f.IsActive,
                LastFetched = f.LastFetched,
                RefreshIntervalSeconds = f.RefreshIntervalSeconds,
                OperatorName = f.Operator.Name
            })
            .ToListAsync(ct);
    }

    public async Task<FeedDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        return await _db.Feeds
            .Include(f => f.Operator)
            .Where(f => f.Id == id)
            .Select(f => new FeedDto
            {
                Id = f.Id,
                FeedType = f.FeedType.ToString(),
                FeedId = f.FeedId,
                ExternalUrl = f.ExternalUrl,
                InternalUrl = f.InternalUrl,
                IsActive = f.IsActive,
                LastFetched = f.LastFetched,
                RefreshIntervalSeconds = f.RefreshIntervalSeconds,
                OperatorName = f.Operator.Name
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Feed> CreateAsync(int operatorId, FeedType feedType, SourceType sourceType, string feedId, string? externalUrl, int refreshIntervalSeconds, CancellationToken ct)
    {
        return await _feedImport.RegisterFeedAsync(operatorId, feedType, sourceType, feedId, externalUrl, null, refreshIntervalSeconds, ct);
    }

    public async Task<(bool Success, string? Message)> UpdateAsync(int id, Feed updated, CancellationToken ct)
    {
        var feed = await _db.Feeds.FindAsync(new object[] { id }, ct);
        if (feed is null) return (false, "Feed not found.");

        feed.FeedType = updated.FeedType;
        feed.ExternalUrl = updated.ExternalUrl;
        feed.InternalUrl = updated.InternalUrl;
        feed.IsActive = updated.IsActive;
        feed.RefreshIntervalSeconds = updated.RefreshIntervalSeconds;

        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<bool> DeactivateAsync(int id, CancellationToken ct)
    {
        var feed = await _db.Feeds.FindAsync(new object[] { id }, ct);
        if (feed is null) return false;

        feed.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
