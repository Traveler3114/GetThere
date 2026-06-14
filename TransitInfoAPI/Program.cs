using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Core;
using TransitInfoAPI.Data;
using TransitInfoAPI.Services;
using TransitInfoAPI.Services.Converters;
using TransitInfoAPI.Services.Otp;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5000");

builder.Services.AddControllers();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");

builder.Services.AddDbContext<TransitDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("otp", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient("gtfsrt", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddScoped<FeedImportService>();
builder.Services.AddScoped<ReconciliationService>();
builder.Services.AddSingleton<OtpManagerService>();
builder.Services.AddScoped<MobilityService>();

builder.Services.AddScoped<StationService>();
builder.Services.AddScoped<RouteService>();
builder.Services.AddScoped<OperatorService>();
builder.Services.AddScoped<FeedService>();
builder.Services.AddScoped<RealtimeService>();

var converterRegistry = new ConverterRegistry();
builder.Services.AddSingleton(converterRegistry);
builder.Services.AddSingleton<ProtobufFeedBuilder>();
builder.Services.AddHostedService<BackgroundConverterWorker>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

Console.WriteLine("""

  TransitInfoAPI
     http://localhost:5000

  OTP will auto-start in a separate terminal window immediately.

""");

await app.StartAsync();

// Generate OTP config on startup
try
{
    var otpManager = app.Services.GetRequiredService<OtpManagerService>();
    await otpManager.GenerateConfigAsync();
    Console.WriteLine("OTP config generated on startup.");
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to generate OTP config on startup: {ex.Message}");
}

if (ShouldAutoStartOtp(app.Configuration))
{
    Console.WriteLine("Starting OTP in a separate terminal window...");
    var otpManager = app.Services.GetRequiredService<OtpManagerService>();

    var process = TryStartOtp(app.Configuration, app.Environment.ContentRootPath);
    if (process is not null)
    {
        otpManager.SetProcess(process);
        Console.WriteLine("OTP started with PID {0}.", process.Id);
    }
    else
    {
        Console.WriteLine("Failed to open a separate terminal for OTP. Start OTP manually.");
    }
}
else
{
    Console.WriteLine("OTP auto-start disabled via configuration (Otp:AutoStart=false).");
}

await app.WaitForShutdownAsync();

static bool ShouldAutoStartOtp(IConfiguration configuration)
{
    var rawValue = configuration["Otp:AutoStart"];
    if (string.IsNullOrWhiteSpace(rawValue)) return true;
    if (bool.TryParse(rawValue, out var autoStart)) return autoStart;
    Console.WriteLine($"Invalid Otp:AutoStart value '{rawValue}'. OTP auto-start disabled.");
    return false;
}

static Process? TryStartOtp(IConfiguration configuration, string contentRootPath)
{
    var javaExecutable = string.IsNullOrWhiteSpace(configuration["Otp:JavaExecutable"])
        ? "java" : configuration["Otp:JavaExecutable"]!;
    var otpJarPath = string.IsNullOrWhiteSpace(configuration["Otp:JarPath"])
        ? "otp-shaded-2.9.0.jar" : configuration["Otp:JarPath"]!;
    var otpArguments = string.IsNullOrWhiteSpace(configuration["Otp:Arguments"])
        ? $"-Xmx2G -jar \"{otpJarPath}\" --build --serve ."
        : configuration["Otp:Arguments"]!;
    var workingDirectory = string.IsNullOrWhiteSpace(configuration["Otp:WorkingDirectory"])
        ? contentRootPath : configuration["Otp:WorkingDirectory"]!;

    try
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StartOtpWindows(javaExecutable, otpArguments, workingDirectory)
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? StartOtpMac(javaExecutable, otpArguments, workingDirectory)
                : StartOtpLinux(javaExecutable, otpArguments, workingDirectory);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"OTP startup error: {ex.Message}");
        return null;
    }
}

static Process? StartOtpWindows(string javaExecutable, string otpArguments, string workingDirectory)
{
    return Process.Start(new ProcessStartInfo
    {
        FileName = "cmd.exe",
        Arguments = $"/c start \"OTP\" /D {QuoteForCmd(workingDirectory)} {QuoteForCmd(javaExecutable)} {otpArguments}",
        UseShellExecute = false,
        CreateNoWindow = true
    });
}

static Process? StartOtpMac(string javaExecutable, string otpArguments, string workingDirectory)
{
    var shellCommand = $"cd {QuoteForBash(workingDirectory)} && {QuoteForBash(javaExecutable)} {otpArguments}";
    var appleScript = $"tell application \"Terminal\" to do script \"{EscapeForAppleScript(shellCommand)}\"";
    var info = new ProcessStartInfo("osascript") { UseShellExecute = false };
    info.ArgumentList.Add("-e");
    info.ArgumentList.Add(appleScript);
    return Process.Start(info);
}

static Process? StartOtpLinux(string javaExecutable, string otpArguments, string workingDirectory)
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
            if (process is not null) return process;
        }
        catch { }
    }
    return null;
}

static string QuoteForCmd(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
static string QuoteForBash(string value) => $"'{value.Replace("'", "'\"'\"'")}'";
static string EscapeForAppleScript(string value) => value
    .Replace("\\", "\\\\")
    .Replace("\"", "\\\"");
