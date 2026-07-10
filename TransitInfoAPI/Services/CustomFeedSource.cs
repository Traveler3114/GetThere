using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Core;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;

namespace TransitInfoAPI.Services;

public class CustomFeedSource : IFeedSource
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CustomFeedEngine _engine;
    private readonly ILogger<CustomFeedSource> _logger;

    public CustomFeedSource(
        IServiceScopeFactory scopeFactory,
        CustomFeedEngine engine,
        ILogger<CustomFeedSource> logger)
    {
        _scopeFactory = scopeFactory;
        _engine = engine;
        _logger = logger;
    }

    public async Task<FeedFetchResult> FetchDataAsync(Feed feed, CancellationToken ct)
    {
        if (feed.CustomFeedId is null)
            throw new InvalidOperationException($"Feed {feed.Id} has no CustomFeedId");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();

        var customFeed = await db.CustomFeeds
            .Include(f => f.FieldMappings)
            .Include(f => f.TableConfigs)
                .ThenInclude(t => t.FieldMappings)
            .FirstOrDefaultAsync(f => f.Id == feed.CustomFeedId.Value, ct);

        if (customFeed is null)
            throw new InvalidOperationException($"CustomFeed {feed.CustomFeedId} not found");

        var run = new CustomFeedRun
        {
            CustomFeedId = customFeed.Id,
            StartedAt = DateTime.UtcNow,
            Status = CustomFeedRunStatus.Running,
            RecordsProduced = 0,
            LogText = string.Empty
        };

        db.CustomFeedRuns.Add(run);
        await db.SaveChangesAsync(ct);

        byte[] outputData;
        int recordsWritten;
        var alreadyHandled = false;

        try
        {
            var engineResult = await _engine.ExecuteAsync(customFeed, ct);

            switch (customFeed.OutputFormat)
            {
                case OutputFormat.GtfsStatic:
                    var mappedRecords = engineResult.TableRecords;
                    if (mappedRecords is null)
                    {
                        mappedRecords = new Dictionary<string, List<Dictionary<string, object?>>>
                        {
                            [customFeed.TargetTable ?? "stops"] = engineResult.Records
                        };
                    }
                    var importer = scope.ServiceProvider
                        .GetRequiredService<CustomFeedDirectImporter>();
                    await importer.ImportAndActivateAsync(customFeed, mappedRecords, ct);
                    outputData = [];
                    recordsWritten = engineResult.RecordCount;
                    alreadyHandled = true;
                    break;

                case OutputFormat.Gbfs:
                    {
                        var mobility = scope.ServiceProvider.GetRequiredService<MobilityManager>();
                        await mobility.UpsertStationsFromRecordsAsync(customFeed.OperatorId, engineResult.Records, ct);
                        outputData = [];
                        recordsWritten = engineResult.RecordCount;
                        alreadyHandled = true;
                        break;
                    }

                case OutputFormat.GtfsRealtime:
                    _logger.LogWarning("GtfsRealtime custom feeds should be polled via RealtimeManager, not CustomFeedSource");
                    outputData = [];
                    recordsWritten = 0;
                    alreadyHandled = true;
                    break;

                default:
                    throw new InvalidOperationException($"Unknown output format: {customFeed.OutputFormat}");
            }

            run.Status = CustomFeedRunStatus.Success;
            run.CompletedAt = DateTime.UtcNow;
            run.RecordsProduced = recordsWritten;
            run.LogText = string.Join("\n", engineResult.LogLines);
            customFeed.LastRunAt = DateTime.UtcNow;

            if (alreadyHandled)
                _logger.LogInformation(
                    "CustomFeedSource: feed {CustomFeedId} imported {Records} records directly",
                    customFeed.Id, recordsWritten);
            else if (outputData.Length > 0)
                _logger.LogInformation(
                    "CustomFeedSource: feed {CustomFeedId} produced {Length} bytes ({Records} records)",
                    customFeed.Id, outputData.Length, recordsWritten);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CustomFeedSource: feed {CustomFeedId} execution failed", customFeed.Id);
            run.Status = CustomFeedRunStatus.Failed;
            run.CompletedAt = DateTime.UtcNow;
            run.LogText = $"Exception: {ex.Message}\n{ex.StackTrace}";
            await db.SaveChangesAsync(ct);
            throw;
        }

        await db.SaveChangesAsync(ct);
        return new FeedFetchResult(outputData, null, null, null) { AlreadyHandled = alreadyHandled };
    }

    public string ComputeHash(Feed feed, byte[] data)
    {
        var hash = SHA1.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
