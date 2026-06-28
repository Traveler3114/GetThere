using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Contracts;
using TransitInfoAPI.Mapping;
namespace TransitInfoAPI.Managers;

public class CustomFeedManager
{
    private readonly TransitDbContext _db;
    private readonly CustomFeedEngine _engine;
    private readonly OnestopIdManager _onestopId;
    private readonly FeedManager _feedManager;
    private readonly Services.FeedSourceFactory _feedSourceFactory;
    private readonly ILogger<CustomFeedManager> _logger;

    public CustomFeedManager(
        TransitDbContext db,
        CustomFeedEngine engine,
        OnestopIdManager onestopId,
        FeedManager feedManager,
        Services.FeedSourceFactory feedSourceFactory,
        ILogger<CustomFeedManager> logger)
    {
        _db = db;
        _engine = engine;
        _onestopId = onestopId;
        _feedManager = feedManager;
        _feedSourceFactory = feedSourceFactory;
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

        // If OutputFormat changed, recreate hidden Feed with new type
        if (request.OutputFormat is not null)
        {
            var hasHiddenFeed = await _db.Feeds.AnyAsync(f => f.CustomFeedId == feed.Id, ct);
            if (hasHiddenFeed)
                await RemoveHiddenFeedAsync(feed.Id, ct);
            await EnsureHiddenFeedAsync(feed, ct);
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

    public async Task<CustomFeedRunResponse> ExecuteAsync(int customFeedId, CancellationToken ct)
    {
        var customFeed = await _db.CustomFeeds
            .FirstOrDefaultAsync(f => f.Id == customFeedId, ct)
            ?? throw new InvalidOperationException($"Custom feed {customFeedId} not found");

        var hiddenFeed = await _db.Feeds
            .FirstOrDefaultAsync(f => f.CustomFeedId == customFeedId, ct);

        if (hiddenFeed is null)
            throw new InvalidOperationException($"No hidden feed found for custom feed {customFeedId}");

        var source = _feedSourceFactory.Resolve(hiddenFeed);
        await source.FetchDataAsync(hiddenFeed, ct);

        var lastRun = await _db.CustomFeedRuns
            .Where(r => r.CustomFeedId == customFeedId)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct);

        return lastRun is null
            ? throw new InvalidOperationException("Run completed but no run record found")
            : CustomFeedMapper.ToRunResponse(lastRun);
    }

    public async Task<CustomFeedDiscoverResponse> DiscoverAsync(CreateCustomFeedRequest request, CancellationToken ct)
    {
        var tempConfig = new CustomFeed
        {
            Name = request.Name,
            BaseUrl = request.BaseUrl,
            HttpMethod = string.IsNullOrWhiteSpace(request.HttpMethod) ? "GET" : request.HttpMethod.ToUpperInvariant(),
            AuthConfig = request.AuthConfig,
            ResponseFormat = Enum.Parse<ResponseFormat>(request.ResponseFormat, true),
            PaginationConfig = request.PaginationConfig,
        };

        if (tempConfig.ResponseFormat != ResponseFormat.JSON)
            throw new InvalidOperationException("Discover only supports JSON response format");

        return await _engine.DiscoverStructureAsync(tempConfig, ct);
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
        if (await _db.Feeds.AnyAsync(f => f.CustomFeedId == feed.Id, ct)) return;

        // For GBFS feeds without a provider, auto-create one for the operator
        if (feed.OutputFormat == OutputFormat.Gbfs && feed.MobilityProviderId is null)
        {
            var provider = new MobilityProvider
            {
                Name = feed.Name,
                FeedFormat = FeedFormat.GBFS,
                OperatorId = feed.OperatorId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.MobilityProviders.Add(provider);
            await _db.SaveChangesAsync(ct);
            feed.MobilityProviderId = provider.Id;
            _logger.LogInformation("Auto-created MobilityProvider {ProviderId} for GBFS custom feed {FeedId}", provider.Id, feed.Id);
        }

        var feedType = feed.OutputFormat switch
        {
            OutputFormat.GtfsStatic => FeedType.GTFSStatic,
            OutputFormat.Gbfs => FeedType.GBFS,
            OutputFormat.GtfsRealtime => FeedType.GTFSRealtime,
            _ => throw new InvalidOperationException($"Unknown output format: {feed.OutputFormat}")
        };
        var suffix = feed.OutputFormat switch
        {
            OutputFormat.GtfsStatic => "gtfs-static",
            OutputFormat.Gbfs => "gbfs",
            OutputFormat.GtfsRealtime => "gtfs-rt",
            _ => "custom"
        };
        var slug = _onestopId.ToNameSlug(feed.Name);
        var hiddenFeed = new Feed
        {
            OperatorId = feed.OperatorId,
            FeedType = feedType,
            IsInternal = true,
            IsActive = true,
            FeedId = $"custom-{feed.Id}-{slug}",
            OnestopId = _onestopId.GenerateFeedOnestopId(0, 0, $"custom-{feed.Id}-{slug}-{suffix}"),
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
        await _db.SaveChangesAsync(ct);
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
