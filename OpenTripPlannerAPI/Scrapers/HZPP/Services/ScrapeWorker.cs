using transit_realtime;
using OpenTripPlannerAPI.Scrapers.HZPP.Models;

namespace OpenTripPlannerAPI.Scrapers.HZPP.Services;

public class ScrapeWorker : BackgroundService
{
    private readonly ILogger<ScrapeWorker> _logger;
    private readonly GtfsLoader _gtfsLoader;
    private readonly HzppScraper _scraper;
    private readonly GtfsFeedStore _feedStore;
    private readonly IConfiguration _config;

    public ScrapeWorker(
        ILogger<ScrapeWorker> logger,
        GtfsLoader gtfsLoader,
        HzppScraper scraper,
        GtfsFeedStore feedStore,
        IConfiguration config)
    {
        _logger = logger;
        _gtfsLoader = gtfsLoader;
        _scraper = scraper;
        _feedStore = feedStore;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var gtfsUrl = _config["Gtfs:ZipUrl"] ?? "https://www.hzpp.hr/GTFS_files.zip";
        var interval = int.Parse(_config["Scrape:IntervalSeconds"] ?? "30");
        var delay = double.Parse(_config["Scrape:RequestDelaySeconds"] ?? "0.3");

        _logger.LogInformation("Loading GTFS from {Url}", gtfsUrl);
        var gtfs = await _gtfsLoader.LoadAsync(gtfsUrl, stoppingToken);

        _logger.LogInformation("Scrape loop started — interval={Interval}s", interval);

        bool firstCycle = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOneCycleAsync(gtfs, delay, stoppingToken);
                if (firstCycle)
                {
                    firstCycle = false;
                    GtfsReadySignal.SetReady();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Scrape loop error"); }

            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
        }
    }

    private async Task RunOneCycleAsync(GtfsData gtfs, double requestDelay, CancellationToken ct)
    {
        var activeTrains = _gtfsLoader.GetActiveTrainNumbers(gtfs);
        int total = activeTrains.Count;
        int ok = 0;
        int current = 0;

        var updatesMap = new Dictionary<string, List<StopTimeUpdateDto>>();

        foreach (var tnum in activeTrains)
        {
            current++;
            Console.Write($"\r⏳ Scraping trains... {current}/{total}");

            var payload = await _scraper.FetchTrainDataAsync(tnum, ct);
            await Task.Delay(TimeSpan.FromSeconds(requestDelay), ct);

            if (payload is null) continue;

            // Skip trains with no delay and no known position — nothing useful to report
            if (payload.DelayMin == 0 && string.IsNullOrEmpty(payload.CurrentStation) && !payload.Finished)
                continue;

            var tripId = _gtfsLoader.GetActiveTripId(tnum, gtfs);
            if (tripId is null) { _logger.LogDebug("No active trip for train {Train} today", tnum); continue; }

            var stus = _scraper.ComputeStopTimeUpdates(tripId, payload, gtfs);
            if (stus.Count > 0) { updatesMap[tripId] = stus; ok++; }
        }

        Console.WriteLine($"\r✅ Scrape done — {ok}/{total} trains with updates at {DateTime.Now:HH:mm:ss}");

        _feedStore.Update(BuildFeed(updatesMap).ToByteArray());
    }

    private static FeedMessage BuildFeed(Dictionary<string, List<StopTimeUpdateDto>> updatesMap)
    {
        var feed = new FeedMessage
        {
            Header = new FeedHeader
            {
                GtfsRealtimeVersion = "2.0",
                Incrementality = FeedHeader.Types.Incrementality.FullDataset,
                Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }
        };

        foreach (var (tripId, stus) in updatesMap)
        {
            if (stus.Count == 0) continue;
            var entity = new FeedEntity
            {
                Id = tripId,
                TripUpdate = new TripUpdate
                {
                    Trip = new TripDescriptor { TripId = tripId }
                }
            };

            foreach (var stu in stus)
                entity.TripUpdate.StopTimeUpdate.Add(new TripUpdate.Types.StopTimeUpdate
                {
                    StopSequence = (uint)stu.StopSequence,
                    StopId = stu.StopId,
                    Arrival = new TripUpdate.Types.StopTimeEvent { Delay = stu.DelaySec },
                    Departure = new TripUpdate.Types.StopTimeEvent { Delay = stu.DelaySec }
                });

            feed.Entity.Add(entity);
        }

        return feed;
    }
}