using System.Diagnostics;
using OpenTripPlannerAPI.Scrapers.HZPP.Services;
using OpenTripPlannerAPI.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5000");

// ── HTTP clients ──────────────────────────────────────────────────────────────
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

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<GtfsFeedStore>();
builder.Services.AddSingleton<GtfsLoader>();
builder.Services.AddSingleton<HzppScraper>();
builder.Services.AddSingleton<DbBackedOtpConfigState>();
builder.Services.AddHostedService<ScrapeWorker>();
builder.Services.AddControllers();
builder.Services.AddSingleton<DbBackedOtpConfigLoader>();

var app = builder.Build();
app.MapControllers();

var configLoader = app.Services.GetRequiredService<DbBackedOtpConfigLoader>();
var configResult = await configLoader.LoadAndGenerateAsync();

Console.WriteLine("""

🚆 OpenTripPlannerAPI (OTP + HŽPP scraper)
   Feed endpoint : http://localhost:5000/hzpp-rt
   Status page   : http://localhost:5000/status

""");

// Start the web server and background scraper
await app.StartAsync();

if (configResult.UsesLocalHzppScraper)
{
    // Wait until the first full scrape cycle is complete and feed is ready
    Console.WriteLine("⏳ Waiting for first scrape cycle to complete...");
    await GtfsReadySignal.WaitAsync();
    Console.WriteLine("✅ First scrape cycle complete, feed is ready!");
}
else
{
    Console.WriteLine("ℹ️  No operators use local HZPP GTFS-RT endpoint — skipping scraper warmup wait.");
}

// Now launch OTP Java
var otpJar = Path.Combine(Directory.GetCurrentDirectory(), "otp-shaded-2.9.0.jar");

if (!File.Exists(otpJar))
{
    Console.WriteLine($"⚠️  OTP jar not found at {otpJar} — skipping OTP launch.");
}
else
{
    var configuredJavaPath = builder.Configuration["Otp:JavaExecutable"];
    var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
    var javaExecutable = !string.IsNullOrWhiteSpace(configuredJavaPath)
        ? configuredJavaPath
        : !string.IsNullOrWhiteSpace(javaHome)
            ? Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java")
            : "java";

    Console.WriteLine("🚀 Starting OpenTripPlanner...");
    var otpProcess = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = javaExecutable,
            Arguments = $"-Xmx2G -jar \"{otpJar}\" --build --serve .",
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        }
    };
    otpProcess.Start();
    Console.WriteLine($"   OTP running (PID {otpProcess.Id})");
}

// Keep the app running
await app.WaitForShutdownAsync();
