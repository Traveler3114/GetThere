using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Services.Otp;

public class OtpManagerService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OtpManagerService> _logger;
    private Process? _otpProcess;

    public OtpManagerService(IServiceScopeFactory scopeFactory, ILogger<OtpManagerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void SetProcess(Process? process)
    {
        _otpProcess = process;
    }

    public async Task GenerateConfigAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransitDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var operators = await db.Operators
            .Include(o => o.Country)
            .Include(o => o.Feeds)
            .Where(o => o.Feeds.Any(f => f.IsActive))
            .ToListAsync(ct);

        var buildConfig = new OtpBuildConfig
        {
            transitFeeds = operators
                .SelectMany(o => o.Feeds
                    .Where(f => f.IsActive && f.FeedType == FeedType.GTFSStatic)
                    .Select(f => new OtpTransitFeedConfig
                    {
                        type = "gtfs",
                        source = f.ExternalUrl ?? f.InternalUrl ?? string.Empty,
                        feedId = f.FeedId
                    }))
                .Where(f => !string.IsNullOrWhiteSpace(f.source))
                .ToList(),
            transitModelTimeZone = configuration["Otp:TimeZone"] ?? "Europe/Zagreb"
        };

        var updaters = operators
            .SelectMany(o => o.Feeds
                .Where(f => f.IsActive && f.FeedType == FeedType.GTFSRealtime)
                .Select(f => new OtpRouterUpdaterConfig
                {
                    type = "STOP_TIME_UPDATER",
                    feedId = f.FeedId,
                    url = f.InternalUrl ?? f.ExternalUrl ?? string.Empty,
                    frequency = "PT30S"
                }))
            .Where(u => !string.IsNullOrWhiteSpace(u.url))
            .ToList();

        var routerConfig = new OtpRouterConfig { updaters = updaters };

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var outputDir = AppContext.BaseDirectory;

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "build-config.json"),
            JsonSerializer.Serialize(buildConfig, jsonOptions), ct);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "router-config.json"),
            JsonSerializer.Serialize(routerConfig, jsonOptions), ct);

        _logger.LogInformation("OTP config generated with {FeedCount} feeds and {UpdaterCount} updaters",
            buildConfig.transitFeeds.Count, updaters.Count);
    }

    public async Task<bool> IsOtpHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var baseUrl = configuration["Otp:InstanceBaseUrl"] ?? "http://localhost:8080";
            var response = await http.GetAsync($"{baseUrl}/otp/routers/default/index", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task RestartOtpAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("OTP restart requested.");

        await GenerateConfigAsync(ct);

        if (_otpProcess is not null && !_otpProcess.HasExited)
        {
            _logger.LogInformation("Killing existing OTP process (PID {Pid})...", _otpProcess.Id);
            try
            {
                _otpProcess.Kill(true);
                _otpProcess.WaitForExit(5000);
                _otpProcess.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill existing OTP process.");
            }
            _otpProcess = null;
        }

        using var scope = _scopeFactory.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var contentRootPath = AppContext.BaseDirectory;

        _otpProcess = StartOtpProcess(configuration, contentRootPath);

        if (_otpProcess is not null)
            _logger.LogInformation("OTP restarted with PID {Pid}", _otpProcess.Id);
        else
            _logger.LogWarning("OTP process could not be started.");
    }

    private Process? StartOtpProcess(IConfiguration configuration, string contentRootPath)
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
            _logger.LogError(ex, "Failed to start OTP process");
            return null;
        }
    }

    private static Process? StartOtpWindows(string javaExecutable, string otpArguments, string workingDirectory)
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"OTP\" /D {QuoteForCmd(workingDirectory)} {QuoteForCmd(javaExecutable)} {otpArguments}",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static Process? StartOtpMac(string javaExecutable, string otpArguments, string workingDirectory)
    {
        var shellCommand = $"cd {QuoteForBash(workingDirectory)} && {QuoteForBash(javaExecutable)} {otpArguments}";
        var appleScript = $"tell application \"Terminal\" to do script \"{EscapeForAppleScript(shellCommand)}\"";
        return Process.Start(new ProcessStartInfo("osascript")
        {
            UseShellExecute = false,
            ArgumentList = { "-e", appleScript }
        });
    }

    private static Process? StartOtpLinux(string javaExecutable, string otpArguments, string workingDirectory)
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

    private static string QuoteForCmd(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string QuoteForBash(string value) => $"'{value.Replace("'", "'\"'\"'")}'";
    private static string EscapeForAppleScript(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"");
}
