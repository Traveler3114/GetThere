using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Exceptions;
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
            .Include(f => f.FieldMappings)
            .Include(f => f.TableConfigs)
                .ThenInclude(t => t.FieldMappings)
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
            .Include(f => f.FieldMappings.OrderBy(m => m.SortOrder))
            .Include(f => f.TableConfigs.OrderBy(t => t.SortOrder))
                .ThenInclude(t => t.FieldMappings.OrderBy(m => m.SortOrder))
            .Include(f => f.Runs.OrderByDescending(r => r.StartedAt).Take(1))
            .FirstOrDefaultAsync(f => f.Id == id, ct);

        return feed is null ? null : CustomFeedMapper.ToResponse(feed);
    }

    public async Task<CustomFeedResponse> CreateAsync(CreateCustomFeedRequest request, CancellationToken ct)
    {
        var feed = new CustomFeed
        {
            OperatorId = request.OperatorId,
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
            }).ToList(),
            TableConfigs = request.TableConfigs.Select((t, i) => new CustomFeedTableConfig
            {
                SortOrder = i + 1,
                Url = t.Url,
                HttpMethod = string.IsNullOrWhiteSpace(t.HttpMethod) ? "GET" : t.HttpMethod.ToUpperInvariant(),
                ResponseFormat = Enum.Parse<ResponseFormat>(t.ResponseFormat, true),
                DataPath = t.DataPath,
                TargetTable = t.TargetTable,
                PaginationConfig = t.PaginationConfig,
                DistinctBy = t.DistinctBy,
                IsStatic = t.IsStatic,
                FieldMappings = t.FieldMappings.Select((m, j) => new CustomFeedTableFieldMapping
                {
                    SortOrder = j + 1,
                    SourceExpression = m.SourceExpression,
                    TargetField = m.TargetField,
                    MappingKind = Enum.Parse<MappingKind>(m.MappingKind, true)
                }).ToList()
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
        if (request.IsScheduleCapable.HasValue) feed.IsScheduleCapable = request.IsScheduleCapable.Value;

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

        // Replace table configs if provided
        if (request.TableConfigs is not null)
        {
            // Load existing table configs with field mappings to remove them
            var existingConfigs = await _db.CustomFeedTableConfigs
                .Include(t => t.FieldMappings)
                .Where(t => t.CustomFeedId == feed.Id)
                .ToListAsync(ct);
            _db.CustomFeedTableConfigs.RemoveRange(existingConfigs);

            feed.TableConfigs = request.TableConfigs.Select((t, i) => new CustomFeedTableConfig
            {
                CustomFeedId = feed.Id,
                SortOrder = i + 1,
                Url = t.Url,
                HttpMethod = string.IsNullOrWhiteSpace(t.HttpMethod) ? "GET" : t.HttpMethod.ToUpperInvariant(),
                ResponseFormat = Enum.Parse<ResponseFormat>(t.ResponseFormat, true),
                DataPath = t.DataPath,
                TargetTable = t.TargetTable,
                PaginationConfig = t.PaginationConfig,
                DistinctBy = t.DistinctBy,
                IsStatic = t.IsStatic,
                FieldMappings = t.FieldMappings.Select((m, j) => new CustomFeedTableFieldMapping
                {
                    SortOrder = j + 1,
                    SourceExpression = m.SourceExpression,
                    TargetField = m.TargetField,
                    MappingKind = Enum.Parse<MappingKind>(m.MappingKind, true)
                }).ToList()
            }).ToList();
        }

        await _db.SaveChangesAsync(ct);

        // If OutputFormat changed, recreate hidden Feed with new type
        if (request.OutputFormat is not null)
        {
            var hasHiddenFeed = await _db.Feeds.AnyAsync(f => f.CustomFeedId == feed.Id, ct);
            if (hasHiddenFeed)
            {
                var hasVersions = await _db.FeedVersions.AnyAsync(fv => fv.Feed.CustomFeedId == feed.Id, ct);
                if (hasVersions)
                    await DeactivateHiddenFeedAsync(feed.Id, ct);
                else
                    await RemoveHiddenFeedAsync(feed.Id, ct);
            }
            await EnsureHiddenFeedAsync(feed, ct);
        }

        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var feed = await _db.CustomFeeds
            .FirstOrDefaultAsync(f => f.Id == id, ct);

        if (feed is null) return false;

        await RemoveHiddenFeedAsync(feed.Id, ct);
        _db.CustomFeeds.Remove(feed);
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
            ?? throw new AppException($"Custom feed {customFeedId} not found", statusCode: 404, errorCode: "NotFound");

        var hiddenFeed = await _db.Feeds
            .FirstOrDefaultAsync(f => f.CustomFeedId == customFeedId, ct);

        if (hiddenFeed is null)
            throw new AppException($"No hidden feed found for custom feed {customFeedId}", statusCode: 404, errorCode: "NotFound");

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
            }).ToList(),
            TableConfigs = request.TableConfigs.Select((t, i) => new CustomFeedTableConfig
            {
                SortOrder = i + 1,
                Url = t.Url,
                HttpMethod = string.IsNullOrWhiteSpace(t.HttpMethod) ? "GET" : t.HttpMethod.ToUpperInvariant(),
                ResponseFormat = Enum.Parse<ResponseFormat>(t.ResponseFormat, true),
                DataPath = t.DataPath,
                TargetTable = t.TargetTable,
                PaginationConfig = t.PaginationConfig,
                DistinctBy = t.DistinctBy,
                IsStatic = t.IsStatic,
                FieldMappings = t.FieldMappings.Select((m, j) => new CustomFeedTableFieldMapping
                {
                    SortOrder = j + 1,
                    SourceExpression = m.SourceExpression,
                    TargetField = m.TargetField,
                    MappingKind = Enum.Parse<MappingKind>(m.MappingKind, true)
                }).ToList()
            }).ToList()
        };

        var engineResult = await _engine.ExecuteAsync(tempConfig, ct);

        List<string> columns;
        List<Dictionary<string, object?>> rows;
        int totalRows;

        List<string> tablesPresent = [];
        bool? hardRequirementMet = null;
        bool? softRequirementMet = null;
        string? analysisWarning = null;

        var isGtfsStatic = request.OutputFormat?.Equals("GtfsStatic", StringComparison.OrdinalIgnoreCase) == true;

        if (engineResult.TableRecords is not null)
        {
            var firstTable = engineResult.TableRecords.Values.FirstOrDefault();
            columns = firstTable?.SelectMany(r => r.Keys).Distinct().OrderBy(k => k).ToList() ?? [];
            rows = firstTable?.Take(20).ToList() ?? [];
            totalRows = firstTable?.Count ?? 0;

            tablesPresent = engineResult.TableRecords
                .Where(kvp => kvp.Value.Count > 0)
                .Select(kvp => kvp.Key)
                .OrderBy(k => k)
                .ToList();

            if (isGtfsStatic)
            {
                var hasStops = tablesPresent.Contains("stops");
                var hasRoutes = tablesPresent.Contains("routes");
                var hasTrips = tablesPresent.Contains("trips");
                var hasStopTimes = tablesPresent.Contains("stop_times");
                var hasCalendar = tablesPresent.Contains("calendar");
                var hasCalendarDates = tablesPresent.Contains("calendar_dates");

                hardRequirementMet = hasStops && hasRoutes && hasTrips && hasStopTimes;
                softRequirementMet = hasCalendar || hasCalendarDates;

                if (hardRequirementMet == false)
                {
                    var missing = new List<string>();
                    if (!hasStops) missing.Add("stops");
                    if (!hasRoutes) missing.Add("routes");
                    if (!hasTrips) missing.Add("trips");
                    if (!hasStopTimes) missing.Add("stop_times");
                    analysisWarning = $"Route type cannot be derived — missing table(s): {string.Join(", ", missing)}. " +
                        "Reconciliation will mark all stops as Inactive.";
                }
                else if (softRequirementMet == false)
                {
                    analysisWarning = "No calendar or calendar_dates table — service schedule will be unavailable.";
                }
            }
        }
        else
        {
            columns = engineResult.Records.SelectMany(r => r.Keys).Distinct().OrderBy(k => k).ToList();
            rows = engineResult.Records.Take(20).ToList();
            totalRows = engineResult.Records.Count;

            if (isGtfsStatic && engineResult.Records.Count > 0)
            {
                var targetTable = request.TargetTable ?? "stops";
                tablesPresent = [targetTable];
            }
        }

        return new CustomFeedPreviewResponse
        {
            Columns = columns,
            Rows = rows,
            TotalRows = totalRows,
            LogLines = engineResult.LogLines,
            TablesPresent = tablesPresent,
            HardRequirementMet = hardRequirementMet,
            SoftRequirementMet = softRequirementMet,
            AnalysisWarning = analysisWarning
        };
    }

    private async Task EnsureHiddenFeedAsync(CustomFeed feed, CancellationToken ct)
    {
        if (await _db.Feeds.AnyAsync(f => f.CustomFeedId == feed.Id, ct)) return;

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

        var versionIds = await _db.FeedVersions
            .Where(fv => fv.FeedId == hidden.Id)
            .Select(fv => fv.Id)
            .ToListAsync(ct);

        foreach (var vid in versionIds)
        {
            await _db.StopTimes.Where(st => st.Trip.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.CalendarDates.Where(cd => cd.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.Calendars.Where(c => c.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.Shapes.Where(s => s.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.Trips.Where(t => t.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.ReconciliationCandidates.Where(rc => rc.RawStop.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.RawStops.Where(rs => rs.FeedVersionId == vid).ExecuteDeleteAsync(ct);
            await _db.Agencies.Where(a => a.FeedVersionId == vid).ExecuteDeleteAsync(ct);
        }

        await _db.FeedVersions.Where(fv => versionIds.Contains(fv.Id)).ExecuteDeleteAsync(ct);
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
