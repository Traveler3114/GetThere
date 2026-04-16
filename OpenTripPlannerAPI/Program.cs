using OpenTripPlannerAPI.Core;
using OpenTripPlannerAPI.Scrapers.Base;
using OpenTripPlannerAPI.Scrapers.Hzpp;
using OpenTripPlannerAPI.Services;
using OpenTripPlannerAPI.Workers;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5000");

builder.Services.AddHttpClient("gtfs", c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddHttpClient("hzpp", c =>
{
    c.BaseAddress = new Uri("https://www.hzpp.app");
    c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; HZPP-RT/2.0)");
    c.DefaultRequestHeaders.Add("Accept", "application/json");
    c.DefaultRequestHeaders.Add("Referer", "https://www.hzpp.app");
    c.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient("operator-source", c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddSingleton<GtfsFeedStore>();
builder.Services.AddSingleton<GtfsReadySignal>();
builder.Services.AddSingleton<HzppGtfsLoader>();
builder.Services.AddSingleton<IScraper, HzppScraper>();
builder.Services.AddSingleton<DbBackedOtpConfigState>();
builder.Services.AddHostedService<ScraperWorker>();
builder.Services.AddControllers();
builder.Services.AddSingleton<DbBackedOtpConfigLoader>();

var app = builder.Build();
app.MapControllers();

var configLoader = app.Services.GetRequiredService<DbBackedOtpConfigLoader>();
var configResult = await configLoader.LoadAndGenerateAsync();

Console.WriteLine("""

🚆 OpenTripPlannerAPI scraper host
   Realtime API   : http://localhost:5000/rt/{feedId}
   HZPP compat    : http://localhost:5000/hzpp-rt
   Status page    : http://localhost:5000/status

ℹ️  Run OTP separately in another terminal:
   java -Xmx2G -jar otp-shaded-2.9.0.jar --build --serve .

""");

await app.StartAsync();

if (configResult.LocalScraperFeedIds.Count > 0)
{
    var readySignal = app.Services.GetRequiredService<GtfsReadySignal>();
    Console.WriteLine($"⏳ Waiting for first scrape cycle: {string.Join(", ", configResult.LocalScraperFeedIds)}");
    foreach (var feedId in configResult.LocalScraperFeedIds)
    {
        await readySignal.WaitAsync(feedId);
    }

    Console.WriteLine("✅ First scrape cycle complete for local scraper feeds.");
}
else
{
    Console.WriteLine("ℹ️  No local scraper feed URLs configured in OTP updater config.");
}

await app.WaitForShutdownAsync();
