using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

using TransitInfoAPI.Data;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Services.Otp;

public class OtpManagerService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OtpManagerService> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly string _pidFilePath;
    private Process? _otpProcess;

    public OtpManagerService(IServiceScopeFactory scopeFactory, ILogger<OtpManagerService> logger, IWebHostEnvironment env)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _env = env;
        _pidFilePath = Path.Combine(_env.ContentRootPath, "otp.pid");
        TryKillStaleOtp();
    }

    public void SetProcess(Process? process)
    {
        _otpProcess = process;
        if (process is not null && !process.HasExited)
            WritePidFile(process.Id);
    }

    private void TryKillStaleOtp()
    {
        if (!File.Exists(_pidFilePath)) return;
        try
        {
            var pidText = File.ReadAllText(_pidFilePath).Trim();
            if (int.TryParse(pidText, out var pid))
            {
                var existing = Process.GetProcessById(pid);
                if (!existing.HasExited)
                {
                    _logger.LogInformation("Killing stale OTP process (PID {Pid}) from previous session", pid);
                    existing.Kill(true);
                    existing.WaitForExit(5000);
                }
            }
        }
        catch (ArgumentException) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to kill stale OTP process"); }
        try { File.Delete(_pidFilePath); } catch { }
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
                .SelectMany(f =>
                {
                    var url = f.InternalUrl ?? f.ExternalUrl ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(url)) return [];
                    return new[]
                    {
                        new OtpRouterUpdaterConfig
                        {
                            type = "STOP_TIME_UPDATER",
                            feedId = f.FeedId,
                            url = url,
                            frequency = "PT30S"
                        },
                        new OtpRouterUpdaterConfig
                        {
                            type = "VEHICLE_POSITIONS",
                            feedId = f.FeedId,
                            url = url,
                            frequency = "PT30S"
                        }
                    };
                }))
            .ToList();

        var routerConfig = new OtpRouterConfig { updaters = updaters };

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var outputDir = _env.ContentRootPath;

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

        await KillExistingOtpAsync();
        if (_otpProcess is not null && !_otpProcess.HasExited)
        {
            _logger.LogInformation("Killing tracked OTP process (PID {Pid})...", _otpProcess.Id);
            try
            {
                _otpProcess.Kill(true);
                _otpProcess.WaitForExit(5000);
                _otpProcess.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill tracked OTP process.");
            }
            _otpProcess = null;
        }

        using var scope = _scopeFactory.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        _otpProcess = StartOtpProcess(configuration, _env.ContentRootPath);

        if (_otpProcess is not null)
        {
            WritePidFile(_otpProcess.Id);
            _logger.LogInformation("OTP restarted with PID {Pid}", _otpProcess.Id);
        }
        else
        {
            _logger.LogWarning("OTP process could not be started.");
        }
    }

    private async Task KillExistingOtpAsync()
    {
        if (!File.Exists(_pidFilePath)) return;
        try
        {
            var pidText = await File.ReadAllTextAsync(_pidFilePath);
            if (int.TryParse(pidText.Trim(), out var pid))
            {
                try
                {
                    var existing = Process.GetProcessById(pid);
                    if (!existing.HasExited)
                    {
                        existing.Kill(true);
                        existing.WaitForExit(5000);
                        _logger.LogInformation("Killed OTP process (PID {Pid}) via PID file", pid);
                    }
                }
                catch (ArgumentException) { }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to kill OTP process via PID file"); }
        try { File.Delete(_pidFilePath); } catch { }
    }

    private void WritePidFile(int pid)
    {
        try
        {
            var dir = Path.GetDirectoryName(_pidFilePath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllText(_pidFilePath, pid.ToString());
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to write PID file"); }
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
        var logPath = Path.Combine(workingDirectory, "otp-console.log");
        var batContent = $"@echo off\r\ncd /d {QuoteForCmd(workingDirectory)}\r\n{QuoteForCmd(javaExecutable)} {otpArguments} > {QuoteForCmd(logPath)} 2>&1\r\n";
        var batPath = Path.Combine(Path.GetTempPath(), $"otp_run_{Guid.NewGuid():N}.bat");
        File.WriteAllText(batPath, batContent);
        return Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"OTP\" /D {QuoteForCmd(workingDirectory)} cmd.exe /c {QuoteForCmd(batPath)}",
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
