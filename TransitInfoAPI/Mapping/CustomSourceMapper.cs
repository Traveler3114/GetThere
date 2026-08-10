using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class CustomSourceMapper
{
    /// <summary>
    /// No projection expression here, unlike the other mappers: a source is always loaded with its
    /// requests and their mappings, so there is nothing for EF to trim and a projection would only
    /// stop the nested collections from materialising.
    /// </summary>
    public static CustomSourceResponse ToResponse(CustomSource cs, string? lastRunStatus = null) => new()
    {
        Id = cs.Id,
        OperatorId = cs.OperatorId,
        OperatorName = cs.Operator?.Name ?? string.Empty,
        Name = cs.Name,
        Kind = cs.Kind.ToString(),
        ExtractorKey = cs.ExtractorKey,
        // Presence only. The credential itself never leaves the service — see CustomSourceResponse.
        HasAuth = !string.IsNullOrWhiteSpace(cs.AuthConfig),
        RefreshIntervalSeconds = cs.RefreshIntervalSeconds,
        IsActive = cs.IsActive,
        ServiceWindowStart = cs.ServiceWindowStart,
        ServiceWindowEnd = cs.ServiceWindowEnd,
        CreatedAt = cs.CreatedAt,
        LastRunAt = cs.LastRunAt,
        LastRunStatus = lastRunStatus,
        Requests = [.. cs.Requests
            .OrderBy(r => r.SortOrder)
            .Select(r => new CustomSourceRequestResponse
            {
                Id = r.Id,
                SortOrder = r.SortOrder,
                TargetSection = r.TargetSection.ToString(),
                Url = r.Url,
                HttpMethod = r.HttpMethod,
                Format = r.Format.ToString(),
                DataPath = r.DataPath,
                PaginationConfig = r.PaginationConfig,
                DistinctBy = r.DistinctBy,
                Mappings = [.. r.Mappings
                    .OrderBy(m => m.SortOrder)
                    .Select(m => new CustomSourceMappingResponse
                    {
                        Id = m.Id,
                        SortOrder = m.SortOrder,
                        SourceExpression = m.SourceExpression,
                        TargetField = m.TargetField,
                        Kind = m.Kind.ToString()
                    })]
            })]
    };

    public static CustomSourceRunResponse ToResponse(CustomSourceRun run) => new()
    {
        Id = run.Id,
        CustomSourceId = run.CustomSourceId,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        Status = run.Status.ToString(),
        RecordsProduced = run.RecordsProduced,
        LogText = run.LogText,
        FeedVersionId = run.FeedVersionId
    };
}
