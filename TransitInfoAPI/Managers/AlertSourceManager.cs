using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Exceptions;
using TransitInfoAPI.Services;

namespace TransitInfoAPI.Managers;

public class AlertSourceManager
{
    private readonly TransitDbContext _db;
    private readonly AlertSourceExtractor _extractor;
    private readonly OnestopIdManager _onestopId;

    public AlertSourceManager(TransitDbContext db, AlertSourceExtractor extractor, OnestopIdManager onestopId)
    {
        _db = db;
        _extractor = extractor;
        _onestopId = onestopId;
    }

    public async Task<(List<AlertSourceResponse> Items, int Total)> GetAllAsync(int page, int perPage, CancellationToken ct)
    {
        var query = _db.Feeds
            .AsNoTracking()
            .Include(f => f.AlertSource)
            .Include(f => f.Operator)
            .Where(f => f.FeedType == FeedType.AlertSource && f.AlertSourceId != null)
            .OrderBy(f => f.AlertSource!.SourceKey);

        var total = await query.CountAsync(ct);
        var feeds = await query.Skip((page - 1) * perPage).Take(perPage).ToListAsync(ct);
        var items = feeds.Select(Mapping.AlertSourceMapper.ToResponse).ToList();
        return (items, total);
    }

    public async Task<AlertSourceResponse?> GetByIdAsync(int id, CancellationToken ct)
    {
        var feed = await _db.Feeds
            .AsNoTracking()
            .Include(f => f.AlertSource)
            .Include(f => f.Operator)
            .Where(f => f.FeedType == FeedType.AlertSource && f.AlertSourceId != null)
            .FirstOrDefaultAsync(f => f.AlertSource!.Id == id, ct);
        if (feed is null) return null;
        return Mapping.AlertSourceMapper.ToResponse(feed);
    }

    public async Task<AlertSourceResponse> CreateAsync(CreateAlertSourceRequest request, CancellationToken ct)
    {
        var op = await _db.Operators.FindAsync([request.OperatorId], ct);
        if (op is null)
            throw new AppException("Operator not found.", 404, "OPERATOR_NOT_FOUND");

        var existingKey = await _db.AlertSources.FirstOrDefaultAsync(a => a.SourceKey == request.SourceKey, ct);
        if (existingKey is not null)
            throw new AppException("SourceKey already in use.", 409, "SOURCE_KEY_TAKEN");

        var alertSource = new AlertSource
        {
            SourceKey = request.SourceKey,
            Kind = request.Kind,
            Format = request.Format,
            Url = request.Url,
            ItemSelector = request.ItemSelector,
            TitleSelector = request.TitleSelector,
            DescriptionSelector = request.DescriptionSelector,
            DateSelector = request.DateSelector,
            LinkSelector = request.LinkSelector,
            CategorySelector = request.CategorySelector,
            IntervalMinutes = request.IntervalMinutes,
            CreatedAt = DateTime.UtcNow
        };
        _db.AlertSources.Add(alertSource);
        await _db.SaveChangesAsync(ct);

        var feed = new Feed
        {
            OnestopId = _onestopId.GenerateFeedOnestopId(0, 0, request.SourceKey),
            FeedType = FeedType.AlertSource,
            FeedId = request.SourceKey,
            IsActive = true,
            RefreshIntervalSeconds = request.IntervalMinutes * 60,
            OperatorId = request.OperatorId,
            AlertSourceId = alertSource.Id
        };
        _db.Feeds.Add(feed);
        await _db.SaveChangesAsync(ct);

        // Reload for response
        var loaded = await _db.Feeds
            .AsNoTracking()
            .Include(f => f.AlertSource)
            .Include(f => f.Operator)
            .FirstAsync(f => f.Id == feed.Id, ct);
        return Mapping.AlertSourceMapper.ToResponse(loaded);
    }

    public async Task<AlertSourceResponse?> UpdateAsync(int id, UpdateAlertSourceRequest request, CancellationToken ct)
    {
        var alertSource = await _db.AlertSources.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alertSource is null) return null;

        alertSource.Kind = request.Kind;
        alertSource.Format = request.Format;
        alertSource.Url = request.Url;
        alertSource.ItemSelector = request.ItemSelector;
        alertSource.TitleSelector = request.TitleSelector;
        alertSource.DescriptionSelector = request.DescriptionSelector;
        alertSource.DateSelector = request.DateSelector;
        alertSource.LinkSelector = request.LinkSelector;
        alertSource.CategorySelector = request.CategorySelector;
        alertSource.IntervalMinutes = request.IntervalMinutes;

        var feed = await _db.Feeds.FirstOrDefaultAsync(f => f.AlertSourceId == id, ct);
        if (feed is not null)
        {
            feed.IsActive = request.IsActive;
            feed.RefreshIntervalSeconds = request.IntervalMinutes * 60;
        }

        await _db.SaveChangesAsync(ct);

        if (feed is not null)
        {
            var loaded = await _db.Feeds
                .AsNoTracking()
                .Include(f => f.AlertSource)
                .Include(f => f.Operator)
                .FirstAsync(f => f.Id == feed.Id, ct);
            return Mapping.AlertSourceMapper.ToResponse(loaded);
        }

        // Fallback if no feed (should not happen)
        return null;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var alertSource = await _db.AlertSources.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alertSource is null) return false;

        var feeds = await _db.Feeds.Where(f => f.AlertSourceId == id).ToListAsync(ct);
        foreach (var feed in feeds)
            _db.Feeds.Remove(feed);

        _db.AlertSources.Remove(alertSource);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<AlertSourcePreviewResponse> PreviewAsync(int id, CancellationToken ct)
    {
        var alertSource = await _db.AlertSources.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new AppException("Alert source not found.", 404, "ALERT_SOURCE_NOT_FOUND");

        var (rows, warnings) = await _extractor.ExtractAsync(alertSource, ct);

        var preview = new AlertSourcePreviewResponse
        {
            ItemCount = rows.Count,
            Warnings = warnings
        };

        foreach (var row in rows.Take(25))
        {
            var (title, description, link, dateRaw, category) = AlertSourceMapperService.ExtractCommon(row);
            preview.Items.Add(new AlertPreviewItem
            {
                Title = title,
                Description = description,
                Link = link,
                Date = dateRaw,
                Category = category
            });
        }

        return preview;
    }
}

// Helper alias to avoid collision between Mapping.AlertSourceMapper and Services.AlertSourceMapper
internal static class AlertSourceMapperService
{
    public static (string? Title, string? Description, string? Link, string? DateRaw, string? Category) ExtractCommon(Services.ExtractedRow row)
        => Services.AlertSourceMapper.ExtractCommon(row);
}
