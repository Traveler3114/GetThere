using Microsoft.Extensions.Logging;

namespace TransitInfoAPI.Writers;

/// <summary>
/// Part 1: stub. Logs a warning and returns 0.
/// Part 2 (future): will write records into the FeedManager import pipeline for the given GTFS table.
/// </summary>
public class GtfsStaticWriter
{
    private readonly ILogger<GtfsStaticWriter> _logger;

    public GtfsStaticWriter(ILogger<GtfsStaticWriter> logger)
    {
        _logger = logger;
    }

    public Task<int> WriteAsync(List<Dictionary<string, object?>> records, string? targetTable, int operatorId, CancellationToken ct)
    {
        _logger.LogWarning(
            "GtfsStatic output not yet implemented — would write {Count} records to table '{Table}' for operator {OperatorId}",
            records.Count, targetTable ?? "(unspecified)", operatorId);

        return Task.FromResult(0);
    }
}
