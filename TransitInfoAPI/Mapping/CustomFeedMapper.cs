using System.Linq.Expressions;
using System.Text.Json;

using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Mapping;

public static class CustomFeedMapper
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static Expression<Func<CustomFeed, CustomFeedResponse>> ToResponseExpression =>
        f => new CustomFeedResponse
        {
            Id = f.Id,
            OperatorId = f.OperatorId,
            OperatorName = f.Operator.Name,
            MobilityProviderId = f.MobilityProviderId,
            MobilityProviderName = f.MobilityProvider != null ? f.MobilityProvider.Name : null,
            Name = f.Name,
            BaseUrl = f.BaseUrl,
            HttpMethod = f.HttpMethod,
            AuthConfig = f.AuthConfig != null ? StripAuthSecrets(f.AuthConfig) : null,
            ResponseFormat = f.ResponseFormat.ToString(),
            OutputFormat = f.OutputFormat.ToString(),
            DataPath = f.DataPath,
            TargetTable = f.TargetTable,
            PaginationConfig = f.PaginationConfig,
            RefreshIntervalSeconds = f.RefreshIntervalSeconds,
            IsActive = f.IsActive,
            CreatedAt = f.CreatedAt,
            LastRunAt = f.LastRunAt,
            LastRunStatus = f.Runs
                .OrderByDescending(r => r.StartedAt)
                .Select(r => r.Status.ToString())
                .FirstOrDefault(),
            FieldMappings = f.FieldMappings
                .OrderBy(m => m.SortOrder)
                .Select(m => new FieldMappingResponse
                {
                    Id = m.Id,
                    CustomFeedId = m.CustomFeedId,
                    SortOrder = m.SortOrder,
                    SourceExpression = m.SourceExpression,
                    TargetField = m.TargetField,
                    MappingKind = m.MappingKind.ToString()
                })
                .ToList()
        };

    public static CustomFeedResponse ToResponse(CustomFeed f) => new()
    {
        Id = f.Id,
        OperatorId = f.OperatorId,
        OperatorName = f.Operator?.Name ?? string.Empty,
        MobilityProviderId = f.MobilityProviderId,
        MobilityProviderName = f.MobilityProvider?.Name,
        Name = f.Name,
        BaseUrl = f.BaseUrl,
        HttpMethod = f.HttpMethod,
        AuthConfig = f.AuthConfig != null ? StripAuthSecrets(f.AuthConfig) : null,
        ResponseFormat = f.ResponseFormat.ToString(),
        OutputFormat = f.OutputFormat.ToString(),
        DataPath = f.DataPath,
        TargetTable = f.TargetTable,
        PaginationConfig = f.PaginationConfig,
        RefreshIntervalSeconds = f.RefreshIntervalSeconds,
        IsActive = f.IsActive,
        CreatedAt = f.CreatedAt,
        LastRunAt = f.LastRunAt,
        LastRunStatus = f.Runs?
            .MaxBy(r => r.StartedAt)?.Status.ToString(),
        FieldMappings = f.FieldMappings?
            .OrderBy(m => m.SortOrder)
            .Select(m => new FieldMappingResponse
            {
                Id = m.Id,
                CustomFeedId = m.CustomFeedId,
                SortOrder = m.SortOrder,
                SourceExpression = m.SourceExpression,
                TargetField = m.TargetField,
                MappingKind = m.MappingKind.ToString()
            })
            .ToList() ?? []
    };

    public static FieldMappingResponse ToFieldMappingResponse(CustomFeedFieldMapping m) => new()
    {
        Id = m.Id,
        CustomFeedId = m.CustomFeedId,
        SortOrder = m.SortOrder,
        SourceExpression = m.SourceExpression,
        TargetField = m.TargetField,
        MappingKind = m.MappingKind.ToString()
    };

    public static CustomFeedRunResponse ToRunResponse(CustomFeedRun r) => new()
    {
        Id = r.Id,
        CustomFeedId = r.CustomFeedId,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        Status = r.Status.ToString(),
        RecordsProduced = r.RecordsProduced,
        LogText = r.LogText
    };

    public static string? StripAuthSecrets(string? authConfigJson)
    {
        if (string.IsNullOrWhiteSpace(authConfigJson))
            return authConfigJson;

        try
        {
            using var doc = JsonDocument.Parse(authConfigJson);
            var root = doc.RootElement.Clone();
            var dict = new Dictionary<string, JsonElement?>();
            foreach (var prop in root.EnumerateObject())
                dict[prop.Name] = prop.Value;

            var secretFields = new[] { "password", "token", "value", "clientSecret" };
            foreach (var secret in secretFields)
                if (dict.ContainsKey(secret))
                    dict[secret] = null;

            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
            writer.WriteStartObject();
            foreach (var kv in dict)
            {
                if (kv.Value.HasValue)
                {
                    writer.WritePropertyName(kv.Key);
                    kv.Value.Value.WriteTo(writer);
                }
                else
                {
                    writer.WriteNull(kv.Key);
                }
            }
            writer.WriteEndObject();
            writer.Flush();
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch
        {
            return authConfigJson;
        }
    }
}
