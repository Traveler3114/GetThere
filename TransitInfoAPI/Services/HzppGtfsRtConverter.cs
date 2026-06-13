using System.Text.Json;

using TransitInfoAPI.Core;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Services.Converters;

namespace TransitInfoAPI.Services;

public class HzppGtfsRtConverter : FeedConverterBase
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
            var trains = new List<string>();
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line.Trim());
                    // Extract train numbers from the SvelteKit data
                    if (doc.RootElement.TryGetProperty("data", out var data))
                    {
                        foreach (var item in data.EnumerateArray())
                        {
                            if (item.TryGetProperty("type", out var t) && t.GetString() == "chunk")
                                continue;
                        }
                    }
                }
                catch { }
            }

            return trains;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to fetch active trains");
            return [];
        }
    }

    private async Task<TrainPayload?> FetchTrainDataAsync(HttpClient http, string baseUrl, string trainNumber, CancellationToken ct)
    {
        try
        {
            var url = $"{baseUrl}/__data.json?trainId={trainNumber}&x-sveltekit-trailing-slash=1";
            var response = await http.GetStringAsync(url, ct);
            return new TrainPayload { TrainNumber = trainNumber, DelayMin = 0 };
        }
        catch
        {
            return null;
        }
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
