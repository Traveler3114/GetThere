using Microsoft.EntityFrameworkCore;
using OpenTripPlannerAPI.Data;
using OpenTripPlannerAPI.Core;
using OpenTripPlannerAPI.Scrapers.Base;
using OpenTripPlannerAPI.Scrapers.Hzpp;
using OpenTripPlannerAPI.Services;
using OpenTripPlannerAPI.Workers;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");

builder.Services.AddDbContextFactory<OtpReadDbContext>(options =>
    options.UseSqlServer(connectionString)
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

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
   Status page     : http://localhost:5000/status

ℹ️  OTP will auto-start in a separate terminal window immediately.

""");

await app.StartAsync();

if (ShouldAutoStartOtp(app.Configuration))
{
    Console.WriteLine("🚀 Starting OTP in a separate terminal window...");
    var started = TryStartOtpInSeparateTerminal(app.Configuration, app.Environment.ContentRootPath);
    if (!started)
    {
        Console.WriteLine("⚠️  Failed to open a separate terminal for OTP. Start OTP manually.");
    }
}
else
{
    Console.WriteLine("ℹ️  OTP auto-start disabled via configuration (Otp:AutoStart=false).");
}

await app.WaitForShutdownAsync();

static bool ShouldAutoStartOtp(IConfiguration configuration)
{
    var rawValue = configuration["Otp:AutoStart"];
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        return true;
    }

    if (bool.TryParse(rawValue, out var autoStart))
    {
        return autoStart;
    }

    Console.WriteLine($"⚠️  Invalid Otp:AutoStart value '{rawValue}'. OTP auto-start disabled.");
    return false;
}

static bool TryStartOtpInSeparateTerminal(IConfiguration configuration, string contentRootPath)
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
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TryStartOtpWindows(javaExecutable, otpArguments, workingDirectory)
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? TryStartOtpMac(javaExecutable, otpArguments, workingDirectory)
                : TryStartOtpLinux(javaExecutable, otpArguments, workingDirectory);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ OTP startup error: {ex.Message}");
        return false;
    }
}

static bool TryStartOtpWindows(string javaExecutable, string otpArguments, string workingDirectory)
{
    var process = Process.Start(new ProcessStartInfo
    {
        FileName = "cmd.exe",
        Arguments = $"/c start \"OTP\" /D {QuoteForCmd(workingDirectory)} {QuoteForCmd(javaExecutable)} {otpArguments}",
        UseShellExecute = false,
        CreateNoWindow = true
    });

    return process is not null;
}

static bool TryStartOtpMac(string javaExecutable, string otpArguments, string workingDirectory)
{
    var shellCommand = $"cd {QuoteForBash(workingDirectory)} && {QuoteForBash(javaExecutable)} {otpArguments}";
    var appleScript = $"tell application \"Terminal\" to do script \"{EscapeForAppleScript(shellCommand)}\"";

    var info = new ProcessStartInfo("osascript")
    {
        UseShellExecute = false
    };
    info.ArgumentList.Add("-e");
    info.ArgumentList.Add(appleScript);

    var process = Process.Start(info);
    return process is not null;
}

static bool TryStartOtpLinux(string javaExecutable, string otpArguments, string workingDirectory)
{
    var shellCommand = $"cd {QuoteForBash(workingDirectory)} && {QuoteForBash(javaExecutable)} {otpArguments}; exec bash";
    var escapedShellCommand = QuoteForBash(shellCommand);
    var terminalCandidates = new (string FileName, string Arguments)[]
    {
        ("x-terminal-emulator", $"-- bash -lc {escapedShellCommand}"),
        ("gnome-terminal", $"-- bash -lc {escapedShellCommand}"),
        ("konsole", $"-e bash -lc {escapedShellCommand}"),
        ("xfce4-terminal", $"--command \"bash -lc {escapedShellCommand}\""),
        ("xterm", $"-e bash -lc {escapedShellCommand}")
    };

    foreach (var candidate in terminalCandidates)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = candidate.FileName,
                Arguments = candidate.Arguments,
                UseShellExecute = false
            });

            if (process is not null)
            {
                return true;
            }
        }
        catch
        {
            // try next terminal candidate
        }
    }

    return false;
}

static string QuoteForCmd(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

static string QuoteForBash(string value) => $"'{value.Replace("'", "'\"'\"'")}'";

static string EscapeForAppleScript(string value) => value
    .Replace("\\", "\\\\")
    .Replace("\"", "\\\"");