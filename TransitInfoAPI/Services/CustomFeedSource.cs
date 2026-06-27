using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Core;
using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Writers;

namespace TransitInfoAPI.Services;

public class CustomFeedSource : IFeedSource
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CustomFeedEngine _engine;
    private readonly GtfsStaticWriter _gtfsStaticWriter;
    private readonly GtfsRealtimeWriter _gtfsRealtimeWriter;
    private readonly GbfsWriter _gbfsWriter;
    private readonly ILogger<CustomFeedSource> _logger;

    public CustomFeedSource(
        IServiceScopeFactory scopeFactory,
        CustomFeedEngine engine,
        GtfsStaticWriter gtfsStaticWriter,
        GtfsRealtimeWriter gtfsRealtimeWriter,
        GbfsWriter gbfsWriter,
        ILogger<CustomFeedSource> logger)
    {
        _scopeFactory = scopeFactory;
        _engine = engine;
        _gtfsStaticWriter = gtfsStaticWriter;
        _gtfsRealtimeWriter = gtfsRealtimeWriter;
        _gbfsWriter = gbfsWriter;
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

        try
        {
            var engineResult = await _engine.ExecuteAsync(customFeed, ct);

            switch (customFeed.OutputFormat)
            {
                case OutputFormat.GtfsStatic:
                    outputData = await _gtfsStaticWriter.ConvertAsync(
                        engineResult.Records, customFeed.TargetTable, ct);
                    recordsWritten = engineResult.RecordCount;
                    break;

                case OutputFormat.GtfsRealtime:
                    outputData = await _gtfsRealtimeWriter.ConvertAsync(engineResult.Records, ct);
                    recordsWritten = engineResult.RecordCount;
                    break;

                case OutputFormat.Gbfs:
                    outputData = await _gbfsWriter.ConvertAsync(
                        engineResult.Records, customFeed.MobilityProviderId, ct);
                    recordsWritten = engineResult.RecordCount;
                    break;

                default:
                    throw new InvalidOperationException($"Unknown output format: {customFeed.OutputFormat}");
            }

            run.Status = CustomFeedRunStatus.Success;
            run.CompletedAt = DateTime.UtcNow;
            run.RecordsProduced = recordsWritten;
            run.LogText = string.Join("\n", engineResult.LogLines);
            customFeed.LastRunAt = DateTime.UtcNow;

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
        return new FeedFetchResult(outputData, null, null, null);
    }

    public string ComputeHash(Feed feed, byte[] data)
    {
        var hash = SHA1.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
