using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;
using TransitInfoAPI.Writers;

namespace TransitInfoAPI.Managers;

public class CustomFeedManager
{
    private readonly TransitDbContext _db;
    private readonly CustomFeedEngine _engine;
    private readonly GtfsStaticWriter _gtfsStaticWriter;
    private readonly GtfsRealtimeWriter _gtfsRealtimeWriter;
    private readonly GbfsWriter _gbfsWriter;
    private readonly OnestopIdManager _onestopId;
    private readonly FeedManager _feedManager;
    private readonly ILogger<CustomFeedManager> _logger;

    public CustomFeedManager(
        TransitDbContext db,
        CustomFeedEngine engine,
        GtfsStaticWriter gtfsStaticWriter,
        GtfsRealtimeWriter gtfsRealtimeWriter,
        GbfsWriter gbfsWriter,
        OnestopIdManager onestopId,
        FeedManager feedManager,
        ILogger<CustomFeedManager> logger)
    {
        _db = db;
        _engine = engine;
        _gtfsStaticWriter = gtfsStaticWriter;
        _gtfsRealtimeWriter = gtfsRealtimeWriter;
        _gbfsWriter = gbfsWriter;
        _onestopId = onestopId;
        _feedManager = feedManager;
        _logger = logger;
    }

    public async Task<List<CustomFeedResponse>> GetAllAsync(int page, int perPage, CancellationToken ct)
    {
        return await _db.CustomFeeds
            .Include(f => f.Operator)
            .Include(f => f.MobilityProvider)
            .Include(f => f.FieldMappings)
            .Include(f => f.Runs.OrderByDescending(r => r.StartedAt).Take(1))
            .OrderBy(f => f.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(CustomFeedMapper.ToResponseExpression)
            .ToListAsync(ct);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken ct)
    {
        return await _db.CustomFeeds.CountAsync(ct);
    }

    public async Task<CustomFeedResponse?> GetByIdAsync(int id, CancellationToken ct)
    {
        var feed = await _db.CustomFeeds
            .Include(f => f.Operator)
            .Include(f => f.MobilityProvider)
            .Include(f => f.FieldMappings.OrderBy(m => m.SortOrder))
            .Include(f => f.Runs.OrderByDescending(r => r.StartedAt).Take(1))
            .FirstOrDefaultAsync(f => f.Id == id, ct);

        return feed is null ? null : CustomFeedMapper.ToResponse(feed);
    }

    public async Task<CustomFeedResponse> CreateAsync(CreateCustomFeedRequest request, CancellationToken ct)
    {
        var feed = new CustomFeed
        {
            OperatorId = request.OperatorId,
            MobilityProviderId = request.MobilityProviderId,
            Name = request.Name,
            BaseUrl = request.BaseUrl,
            HttpMethod = string.IsNullOrWhiteSpace(request.HttpMethod) ? "GET" : request.HttpMethod.ToUpperInvariant(),
            AuthConfig = request.AuthConfig,
            ResponseFormat = Enum.Parse<ResponseFormat>(request.ResponseFormat, true),
            OutputFormat = Enum.Parse<OutputFormat>(request.OutputFormat, true),
            DataPath = request.DataPath,
            TargetTable = request.TargetTable,
            PaginationConfig = request.PaginationConfig,
            RefreshIntervalSeconds = request.RefreshIntervalSeconds,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            FieldMappings = request.FieldMappings.Select((m, i) => new CustomFeedFieldMapping
            {
                SortOrder = i + 1,
                SourceExpression = m.SourceExpression,
                TargetField = m.TargetField,
                MappingKind = Enum.Parse<MappingKind>(m.MappingKind, true)
            }).ToList()
        };

        _db.CustomFeeds.Add(feed);
        await _db.SaveChangesAsync(ct);

        await EnsureHiddenFeedAsync(feed, ct);

        return (await GetByIdAsync(feed.Id, ct))!;
    }

    public async Task<bool> UpdateAsync(int id, UpdateCustomFeedRequest request, CancellationToken ct)
    {
        var feed = await _db.CustomFeeds
            .Include(f => f.FieldMappings)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

        if (feed is null) return false;

        if (request.OperatorId.HasValue) feed.OperatorId = request.OperatorId.Value;
        if (request.MobilityProviderId.HasValue) feed.MobilityProviderId = request.MobilityProviderId.Value;
        if (request.Name is not null) feed.Name = request.Name;
        if (request.BaseUrl is not null) feed.BaseUrl = request.BaseUrl;
        if (request.HttpMethod is not null) feed.HttpMethod = request.HttpMethod.ToUpperInvariant();
        if (request.ResponseFormat is not null)
            feed.ResponseFormat = Enum.Parse<ResponseFormat>(request.ResponseFormat, true);
        if (request.OutputFormat is not null)
            feed.OutputFormat = Enum.Parse<OutputFormat>(request.OutputFormat, true);
        if (request.DataPath is not null) feed.DataPath = request.DataPath;
        if (request.TargetTable is not null) feed.TargetTable = request.TargetTable;
        if (request.PaginationConfig is not null) feed.PaginationConfig = request.PaginationConfig;
        if (request.RefreshIntervalSeconds.HasValue) feed.RefreshIntervalSeconds = request.RefreshIntervalSeconds.Value;
        if (request.IsActive.HasValue) feed.IsActive = request.IsActive.Value;

        // AuthConfig: null or empty = preserve, valid JSON = replace
        if (request.AuthConfig is not null)
            feed.AuthConfig = request.AuthConfig;

        // Replace field mappings if provided
        if (request.FieldMappings is not null)
        {
            _db.CustomFeedFieldMappings.RemoveRange(feed.FieldMappings);
            feed.FieldMappings = request.FieldMappings.Select((m, i) => new CustomFeedFieldMapping
            {
                CustomFeedId = feed.Id,
                SortOrder = i + 1,
                SourceExpression = m.SourceExpression,
                TargetField = m.TargetField,
                MappingKind = Enum.Parse<MappingKind>(m.MappingKind, true)
            }).ToList();
        }

        await _db.SaveChangesAsync(ct);

        // If OutputFormat changed, create or remove hidden Feed
        if (request.OutputFormat is not null)
        {
            var newFormat = Enum.Parse<OutputFormat>(request.OutputFormat, true);
            var hasHiddenFeed = await _db.Feeds.AnyAsync(f => f.CustomFeedId == feed.Id, ct);

            if (newFormat == OutputFormat.GtfsStatic && !hasHiddenFeed)
                await EnsureHiddenFeedAsync(feed, ct);
            else if (newFormat != OutputFormat.GtfsStatic && hasHiddenFeed)
                await RemoveHiddenFeedAsync(feed.Id, ct);
        }

        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var feed = await _db.CustomFeeds
            .Include(f => f.Runs.Take(1))
            .FirstOrDefaultAsync(f => f.Id == id, ct);

        if (feed is null) return false;

        bool hasRuns = feed.Runs.Count > 0;

        if (hasRuns)
        {
            feed.IsActive = false;
            await DeactivateHiddenFeedAsync(feed.Id, ct);
        }
        else
        {
            await RemoveHiddenFeedAsync(feed.Id, ct);
            _db.CustomFeeds.Remove(feed);
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<CustomFeedRunResponse>> GetRunsAsync(int feedId, int page, int perPage, CancellationToken ct)
    {
        return await _db.CustomFeedRuns
            .Where(r => r.CustomFeedId == feedId)
            .OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(r => new CustomFeedRunResponse
            {
                Id = r.Id,
                CustomFeedId = r.CustomFeedId,
                StartedAt = r.StartedAt,
                CompletedAt = r.CompletedAt,
                Status = r.Status.ToString(),
                RecordsProduced = r.RecordsProduced,
                LogText = r.LogText
            })
            .ToListAsync(ct);
    }

    public async Task<int> GetRunsTotalCountAsync(int feedId, CancellationToken ct)
    {
        return await _db.CustomFeedRuns.CountAsync(r => r.CustomFeedId == feedId, ct);
    }

    public async Task<CustomFeedRunResponse?> GetRunByIdAsync(int runId, CancellationToken ct)
    {
        var run = await _db.CustomFeedRuns
            .FirstOrDefaultAsync(r => r.Id == runId, ct);

        return run is null ? null : CustomFeedMapper.ToRunResponse(run);
    }

    public async Task<CustomFeedRunResponse> ExecuteAsync(int feedId, CancellationToken ct)
    {
        var feed = await _db.CustomFeeds
            .Include(f => f.FieldMappings)
            .FirstOrDefaultAsync(f => f.Id == feedId, ct);

        if (feed is null)
            throw new InvalidOperationException($"Custom feed {feedId} not found");

        var run = new CustomFeedRun
        {
            CustomFeedId = feedId,
            StartedAt = DateTime.UtcNow,
            Status = CustomFeedRunStatus.Running,
            RecordsProduced = 0,
            LogText = string.Empty
        };

        _db.CustomFeedRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        int recordsWritten = 0;

        try
        {
            var engineResult = await _engine.ExecuteAsync(feed, ct);

            switch (feed.OutputFormat)
            {
                case OutputFormat.GtfsStatic:
                    var hiddenFeed = await _db.Feeds.FirstOrDefaultAsync(f => f.CustomFeedId == feed.Id, ct);
                    recordsWritten = await _gtfsStaticWriter.WriteAsync(
                        engineResult.Records, feed.TargetTable, feed.OperatorId,
                        hiddenFeed?.FeedId, ct);
                    if (hiddenFeed is not null && recordsWritten > 0)
                    {
                        await _feedManager.TriggerImportAsync(hiddenFeed.Id, ct);
                    }
                    break;
                case OutputFormat.GtfsRealtime:
                    recordsWritten = await _gtfsRealtimeWriter.WriteAsync(engineResult.Records, ct);
                    break;
                case OutputFormat.Gbfs:
                    recordsWritten = await _gbfsWriter.WriteAsync(engineResult.Records, feed.MobilityProviderId, ct);
                    break;
            }

            run.Status = CustomFeedRunStatus.Success;
            run.CompletedAt = DateTime.UtcNow;
            run.RecordsProduced = recordsWritten;
            run.LogText = string.Join("\n", engineResult.LogLines);
            feed.LastRunAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Custom feed {FeedId} execution failed", feedId);
            run.Status = CustomFeedRunStatus.Failed;
            run.CompletedAt = DateTime.UtcNow;
            run.LogText = $"Exception: {ex.Message}\n{ex.StackTrace}";
        }

        await _db.SaveChangesAsync(ct);
        return CustomFeedMapper.ToRunResponse(run);
    }

    public async Task<CustomFeedPreviewResponse> PreviewAsync(CreateCustomFeedRequest request, CancellationToken ct)
    {
        var tempConfig = new CustomFeed
        {
            Name = request.Name,
            BaseUrl = request.BaseUrl,
            HttpMethod = string.IsNullOrWhiteSpace(request.HttpMethod) ? "GET" : request.HttpMethod.ToUpperInvariant(),
            AuthConfig = request.AuthConfig,
            ResponseFormat = Enum.Parse<ResponseFormat>(request.ResponseFormat, true),
            OutputFormat = Enum.Parse<OutputFormat>(request.OutputFormat, true),
            DataPath = request.DataPath,
            TargetTable = request.TargetTable,
            PaginationConfig = request.PaginationConfig,
            RefreshIntervalSeconds = request.RefreshIntervalSeconds,
            FieldMappings = request.FieldMappings.Select((m, i) => new CustomFeedFieldMapping
            {
                SortOrder = i + 1,
                SourceExpression = m.SourceExpression,
                TargetField = m.TargetField,
                MappingKind = Enum.Parse<MappingKind>(m.MappingKind, true)
            }).ToList()
        };

        var engineResult = await _engine.ExecuteAsync(tempConfig, ct);

        var columns = engineResult.Records
            .SelectMany(r => r.Keys)
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        return new CustomFeedPreviewResponse
        {
            Columns = columns,
            Rows = engineResult.Records.Take(20).ToList(),
            TotalRows = engineResult.Records.Count,
            LogLines = engineResult.LogLines
        };
    }

    private async Task EnsureHiddenFeedAsync(CustomFeed feed, CancellationToken ct)
    {
        if (feed.OutputFormat != OutputFormat.GtfsStatic) return;
        if (await _db.Feeds.AnyAsync(f => f.CustomFeedId == feed.Id, ct)) return;

        var slug = _onestopId.ToNameSlug(feed.Name);
        var hiddenFeed = new Feed
        {
            OperatorId = feed.OperatorId,
            FeedType = FeedType.GTFSStatic,
            IsInternal = true,
            IsActive = true,
            FeedId = $"custom-{feed.Id}-{slug}",
            OnestopId = _onestopId.GenerateFeedOnestopId(0, 0, $"custom-{feed.Id}-{slug}-gtfs-static"),
            RefreshIntervalSeconds = feed.RefreshIntervalSeconds,
            CustomFeedId = feed.Id,
            CreatedAt = DateTime.UtcNow
        };
        _db.Feeds.Add(hiddenFeed);
        await _db.SaveChangesAsync(ct);
    }

    private async Task RemoveHiddenFeedAsync(int customFeedId, CancellationToken ct)
    {
        var hidden = await _db.Feeds.FirstOrDefaultAsync(f => f.CustomFeedId == customFeedId, ct);
        if (hidden is null) return;
        _db.Feeds.Remove(hidden);
    }

    private async Task DeactivateHiddenFeedAsync(int customFeedId, CancellationToken ct)
    {
        var hidden = await _db.Feeds.FirstOrDefaultAsync(f => f.CustomFeedId == customFeedId, ct);
        if (hidden is not null)
        {
            hidden.IsActive = false;
        }
    }
}
