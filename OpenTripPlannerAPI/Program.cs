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
builder.Services.AddSingleton<ProtobufFeedBuilder>();
builder.Services.AddSingleton<DbBackedOtpConfigState>();
builder.Services.AddSingleton<DbBackedOtpConfigLoader>();

builder.Services.AddSingleton<HzppGtfsLoader>();
builder.Services.AddSingleton<IScraper, HzppScraper>();

builder.Services.AddHostedService<ScraperWorker>();
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();

var configLoader = app.Services.GetRequiredService<DbBackedOtpConfigLoader>();
var configResult = await configLoader.LoadAndGenerateAsync();

Console.WriteLine("""

🚆 OpenTripPlannerAPI Scraper Host
   Realtime API    : http://localhost:5000/rt/{feedId}
   HZPP shortcut   : http://localhost:5000/hzpp-rt
   Status page     : http://localhost:5000/status

ℹ️  Run OTP Java in a separate terminal.

""");

await app.StartAsync();

if (configResult.UsesLocalHzppScraper)
{
    Console.WriteLine("⏳ Waiting for first scrape cycle to complete...");
    var readySignal = app.Services.GetRequiredService<GtfsReadySignal>();
    await readySignal.WaitAsync();
    Console.WriteLine("✅ First scrape cycle complete, feed is ready!");
}
else
{
    Console.WriteLine("ℹ️  No operators use local HZPP GTFS-RT endpoint — scraper worker remains idle.");
}

await app.WaitForShutdownAsync();
