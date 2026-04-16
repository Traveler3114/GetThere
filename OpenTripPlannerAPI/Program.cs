using OpenTripPlannerAPI.Core;
using OpenTripPlannerAPI.Scrapers.Base;
using OpenTripPlannerAPI.Scrapers.Hzpp;
using OpenTripPlannerAPI.Services;
using OpenTripPlannerAPI.Workers;
using System.Diagnostics;

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
await configLoader.LoadAndGenerateAsync();

Console.WriteLine("""

🚆 OpenTripPlannerAPI Scraper Host
   Realtime API    : http://localhost:5000/rt/{feedId}
   HZPP shortcut   : http://localhost:5000/hzpp-rt
   Status page     : http://localhost:5000/status

ℹ️  OTP will auto-start after the first scraper cycle completes.

""");

await app.StartAsync();

var readySignal = app.Services.GetRequiredService<GtfsReadySignal>();
Console.WriteLine("⏳ Waiting for first scrape cycle to complete...");
await readySignal.WaitAsync();
Console.WriteLine("✅ First scrape cycle complete, starting OTP...");

if (ShouldAutoStartOtp(app.Configuration))
{
    var started = TryStartOtp(app.Configuration, app.Environment.ContentRootPath);
    if (!started)
    {
        Console.WriteLine("⚠️  Failed to auto-start OTP. Check Otp configuration and try starting OTP manually.");
    }
}
else
{
    Console.WriteLine("ℹ️  OTP auto-start disabled via configuration (Otp:AutoStart=false).");
}

await app.WaitForShutdownAsync();

static bool ShouldAutoStartOtp(IConfiguration configuration)
{
    return !bool.TryParse(configuration["Otp:AutoStart"], out var autoStart) || autoStart;
}

static bool TryStartOtp(IConfiguration configuration, string contentRootPath)
{
    var javaExecutable = string.IsNullOrWhiteSpace(configuration["Otp:JavaExecutable"])
        ? "java"
        : configuration["Otp:JavaExecutable"]!;
    var otpJarPath = string.IsNullOrWhiteSpace(configuration["Otp:JarPath"])
        ? "otp-shaded-2.9.0.jar"
        : configuration["Otp:JarPath"]!;
    var otpArguments = string.IsNullOrWhiteSpace(configuration["Otp:Arguments"])
        ? $"-Xmx2G -jar \"{otpJarPath}\" --build --serve ."
        : configuration["Otp:Arguments"]!;
    var workingDirectory = string.IsNullOrWhiteSpace(configuration["Otp:WorkingDirectory"])
        ? contentRootPath
        : configuration["Otp:WorkingDirectory"]!;

    try
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = javaExecutable,
            Arguments = otpArguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        });

        if (process is null)
        {
            return false;
        }

        Console.WriteLine($"🚀 OTP started (PID {process.Id})");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ OTP startup error: {ex.Message}");
        return false;
    }
}
