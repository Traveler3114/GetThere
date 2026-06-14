using System.Text.Json;
using System.Text.RegularExpressions;

using TransitInfoAPI.Core;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Services.Converters;

namespace TransitInfoAPI.Services;

public partial class HzppGtfsRtConverter : FeedConverterBase
{
    private readonly ProtobufFeedBuilder _feedBuilder;
    private readonly IConfiguration _config;

    public HzppGtfsRtConverter(
        ILogger<HzppGtfsRtConverter> logger,
        IHttpClientFactory httpClientFactory,
        ProtobufFeedBuilder feedBuilder,
        IConfiguration config)
        : base(logger, httpClientFactory)
    {
        _feedBuilder = feedBuilder;
        _config = config;
    }

    public override string ConverterType => "HZPP_GTFSRT";

    public override async Task ConvertAsync(Entities.FeedConverter converterConfig, CancellationToken ct)
    {
        var config = converterConfig.ConverterConfig is not null
            ? JsonSerializer.Deserialize<HzppConverterConfig>(converterConfig.ConverterConfig)
            : new HzppConverterConfig();

        config ??= new HzppConverterConfig();

        Logger.LogInformation("HZPP GTFS-RT converter running for feed {FeedId}", converterConfig.Feed.FeedId);

        var http = HttpClientFactory.CreateClient("hzpp");
        var baseUrl = config.BaseUrl ?? "https://www.hzpp.app";

        var activeTrains = await FetchActiveTrainsAsync(http, baseUrl, ct);
        var updatesMap = new Dictionary<string, List<StopTimeUpdateData>>();

        foreach (var train in activeTrains)
        {
            var payload = await FetchTrainDataAsync(http, baseUrl, train, ct);
            if (payload is null) continue;

            var tripId = $"hzpp-{train}";
            var stus = new List<StopTimeUpdateData>
            {
                new()
                {
                    StopId = $"hzpp-{train}",
                    StopSequence = 1,
                    DelaySec = payload.DelayMin * 60,
                    ScheduledArrivalSec = -1,
                    ScheduledDepartureSec = -1,
                    TripStartDate = DateTime.UtcNow.ToString("yyyyMMdd")
                }
            };

            updatesMap[tripId] = stus;
        }

        var feedBytes = _feedBuilder.BuildFeed(updatesMap).ToByteArray();
        var outputPath = GetOutputPath(converterConfig);
        var outputDir = Path.GetDirectoryName(outputPath);
        if (outputDir is not null) Directory.CreateDirectory(outputDir);

        await File.WriteAllBytesAsync(outputPath, feedBytes, ct);

        Logger.LogInformation("HZPP GTFS-RT conversion complete: {Count} trains", activeTrains.Count);
    }

    private async Task<List<string>> FetchActiveTrainsAsync(HttpClient http, string baseUrl, CancellationToken ct)
    {
        try
        {
            var response = await http.GetStringAsync($"{baseUrl}/__data.json", ct);

            var trains = TryExtractFromSvelteData(response);
            if (trains.Count > 0)
            {
                Logger.LogInformation("Found {Count} trains from __data.json", trains.Count);
                return trains;
            }

            Logger.LogInformation("No trains found in __data.json, trying HTML fallback");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to fetch __data.json, trying HTML fallback");
        }

        try
        {
            var html = await http.GetStringAsync($"{baseUrl}/vlakovi", ct);
            var trains = TryExtractFromHtml(html);
            if (trains.Count > 0)
            {
                Logger.LogInformation("Found {Count} trains from HTML", trains.Count);
                return trains;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to fetch HTML page");
        }

        return [];
    }

    private static List<string> TryExtractFromSvelteData(string json)
    {
        var trains = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);

            // Modern SvelteKit format: {"type":"data","nodes":[{"data":{...}}]}
            if (doc.RootElement.TryGetProperty("nodes", out var nodes))
            {
                foreach (var node in nodes.EnumerateArray())
                {
                    if (node.TryGetProperty("data", out var data))
                    {
                        trains.AddRange(ExtractTrainNumbersFromElement(data));
                    }
                }
            }

            // Newer SvelteKit streaming format: {"type":"data","data":{...}}
            if (doc.RootElement.TryGetProperty("type", out var type) &&
                type.GetString() == "data" &&
                doc.RootElement.TryGetProperty("data", out var dataDirect))
            {
                trains.AddRange(ExtractTrainNumbersFromElement(dataDirect));
            }

            // Try the whole JSON as a flat object with train data
            trains.AddRange(ExtractTrainNumbersFromElement(doc.RootElement));
        }
        catch { }

        return trains.Distinct().ToList();
    }

    private static List<string> ExtractTrainNumbersFromElement(JsonElement element)
    {
        var result = new List<string>();

        try
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var s = item.GetString();
                            if (s is not null && TrainNumberRegex().IsMatch(s))
                                result.Add(s);
                        }
                        else if (item.ValueKind == JsonValueKind.Number)
                        {
                            result.Add(item.GetInt32().ToString());
                        }
                        else if (item.ValueKind == JsonValueKind.Object)
                        {
                            result.AddRange(ExtractTrainNumbersFromElement(item));
                        }
                    }
                    break;

                case JsonValueKind.Object:
                    foreach (var prop in element.EnumerateObject())
                    {
                        if (prop.Value.ValueKind is JsonValueKind.String && TrainNumberRegex().IsMatch(prop.Value.GetString()!))
                            result.Add(prop.Value.GetString()!);
                        else if (prop.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                            result.AddRange(ExtractTrainNumbersFromElement(prop.Value));
                    }
                    break;
            }
        }
        catch { }

        return result;
    }

    private static List<string> TryExtractFromHtml(string html)
    {
        var trains = new List<string>();

        // Look for train number patterns in HTML: typically 4-5 digit numbers
        var regex = TrainNumberRegex();
        foreach (Match match in regex.Matches(html))
        {
            trains.Add(match.Value);
        }

        return trains.Distinct().ToList();
    }

    [GeneratedRegex(@"\b\d{4,5}\b")]
    private static partial Regex TrainNumberRegex();

    private async Task<TrainPayload?> FetchTrainDataAsync(HttpClient http, string baseUrl, string trainNumber, CancellationToken ct)
    {
        try
        {
            var url = $"{baseUrl}/__data.json?trainId={trainNumber}&x-sveltekit-trailing-slash=1";
            var response = await http.GetStringAsync(url, ct);

            using var doc = JsonDocument.Parse(response);
            var delay = TryExtractDelay(doc.RootElement);

            return new TrainPayload { TrainNumber = trainNumber, DelayMin = delay };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to fetch data for train {TrainNumber}", trainNumber);
            return new TrainPayload { TrainNumber = trainNumber, DelayMin = 0 };
        }
    }

    private static int TryExtractDelay(JsonElement element)
    {
        try
        {
            // Look for delay/minutes fields in the JSON
            if (element.TryGetProperty("nodes", out var nodes))
            {
                foreach (var node in nodes.EnumerateArray())
                {
                    if (node.TryGetProperty("data", out var data))
                    {
                        var delay = FindDelayValue(data);
                        if (delay.HasValue) return delay.Value;
                    }
                }
            }

            if (element.TryGetProperty("data", out var dataDirect))
            {
                var delay = FindDelayValue(dataDirect);
                if (delay.HasValue) return delay.Value;
            }
        }
        catch { }

        return 0;
    }

    private static int? FindDelayValue(JsonElement element)
    {
        try
        {
            foreach (var prop in element.EnumerateObject())
            {
                var name = prop.Name.ToLowerInvariant();

                if (name is "delay" or "delayminutes" or "delay_min" or "kasnjenje" or "zakašnjenje")
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                        return prop.Value.GetInt32();
                    if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out var d))
                        return d;
                }

                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    var nested = prop.Value.ValueKind == JsonValueKind.Object
                        ? FindDelayValue(prop.Value)
                        : FindDelayInArray(prop.Value);
                    if (nested.HasValue) return nested;
                }
            }
        }
        catch { }

        return null;
    }

    private static int? FindDelayInArray(JsonElement element)
    {
        try
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var delay = FindDelayValue(item);
                    if (delay.HasValue) return delay;
                }
            }
        }
        catch { }

        return null;
    }

    private class TrainPayload
    {
        public string TrainNumber { get; set; } = string.Empty;
        public int DelayMin { get; set; }
    }
}

public class HzppConverterConfig
{
    public string? BaseUrl { get; set; }
}
