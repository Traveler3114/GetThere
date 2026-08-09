using System.IO.Compression;

using TransitInfoAPI.Core;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Services;

/// <summary>
/// The GTFS-static source: downloads an archive, and reads it into a <see cref="TransitDocument"/>.
/// <para>
/// Everything GTFS-specific lives here rather than in the shared import path — required-file
/// validation, the decompression-bomb ceiling, and the rule that a feed without any calendar data is
/// a failed import. Those are properties of the archive format, not of importing, and leaving them
/// in <c>FeedManager</c> would have imposed them on sources that legitimately carry neither a
/// calendar nor a trip.
/// </para>
/// </summary>
public class GtfsZipSource : ITransitSource
{
    /// <summary>Ceiling on the declared uncompressed size of an archive — decompression-bomb guard.</summary>
    private const long MaxUncompressedArchiveBytes = 4L * 1024 * 1024 * 1024;

    private readonly ExternalFeedSource _external;
    private readonly GtfsParser _gtfs;
    private readonly ImportLogStore _logStore;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<GtfsZipSource> _logger;

    public GtfsZipSource(
        ExternalFeedSource external,
        GtfsParser gtfs,
        ImportLogStore logStore,
        IWebHostEnvironment env,
        ILogger<GtfsZipSource> logger)
    {
        _external = external;
        _gtfs = gtfs;
        _logStore = logStore;
        _env = env;
        _logger = logger;
    }

    public string Kind => "gtfs-zip";

    public bool CanHandle(Feed feed) => feed.FeedType == FeedType.GTFSStatic && feed.Url is not null;

    public async Task<SourceFetchResult?> FetchAsync(Feed feed, CancellationToken ct)
    {
        var result = await _external.FetchDataAsync(feed, ct);

        // Content-type validation for external HTTP responses.
        if (result.ContentType is not null
            && result.ContentType != "application/zip"
            && !result.ContentType.StartsWith("application/", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Feed {FeedId} has unexpected Content-Type {ContentType}, skipping", feed.FeedId, result.ContentType);
            return null;
        }

        var storage = new FeedStorage(_env.ContentRootPath);
        var outputDir = storage.DirectoryFor(feed.FeedId);
        try
        {
            Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create directory {Dir} for feed {FeedId}", outputDir, feed.FeedId);
            return null;
        }

        WriteAtomically(storage.ZipPathFor(feed.FeedId), result.Data, feed.FeedId);

        return new SourceFetchResult(_external.ComputeHash(feed, result.Data), result.Data.Length, result.ContentType);
    }

    private void WriteAtomically(string zipPath, byte[] data, string feedId)
    {
        var tmpPath = zipPath + ".tmp";
        File.WriteAllBytes(tmpPath, data);
        try
        {
            File.Replace(tmpPath, zipPath, null);
        }
        catch (FileNotFoundException)
        {
            // No existing archive to replace — first fetch for this feed.
            File.Move(tmpPath, zipPath);
        }
        catch (IOException ex)
        {
            // File.Replace needs to delete the destination, which fails while anything still holds a
            // handle on it — an import reading the previous archive, a virus scanner, or an editor.
            // Observed live as "Unable to remove the file to be replaced" and "the process cannot
            // access the file", which failed the whole poll for that feed. Move with overwrite does
            // not have the same delete-then-rename requirement.
            _logger.LogWarning(ex, "Could not replace {ZipPath} in place for feed {FeedId}; overwriting instead", zipPath, feedId);
            File.Move(tmpPath, zipPath, overwrite: true);
        }
    }

    public Task<SourceReadResult> OpenAsync(Feed feed, int feedVersionId, CancellationToken ct)
    {
        var zipPath = new FeedStorage(_env.ContentRootPath).ZipPathFor(feed.FeedId);
        if (!File.Exists(zipPath))
            return Task.FromResult(SourceReadResult.Fail("GTFS zip not found on disk"));

        // Copied before reading so a concurrent poll replacing gtfs.zip cannot pull the archive out
        // from under an import that is streaming a million stop_times rows out of it.
        var importTempDir = Path.Combine(Path.GetTempPath(), "gtfs-imports");
        try
        {
            Directory.CreateDirectory(importTempDir);
        }
        catch
        {
            throw new Exceptions.AppException("Failed to create temporary directory for import.", 500, "ImportTempDirFailed");
        }

        var tempZipPath = Path.Combine(importTempDir, $"import-{feedVersionId}-{Guid.NewGuid()}.zip");
        File.Copy(zipPath, tempZipPath, overwrite: true);
        _logStore.AddEntry(feedVersionId, "Copied GTFS zip to temporary working file");

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(tempZipPath);
        }
        catch (Exception ex)
        {
            TryDelete(tempZipPath);
            return Task.FromResult(SourceReadResult.Fail($"Failed to open GTFS archive: {ex.Message}"));
        }

        var cleanup = Cleanup(archive, tempZipPath);

        // A small zip can declare an enormous expansion, and the parser streams entries into memory.
        var declaredUncompressed = archive.Entries.Sum(e => e.Length);
        if (declaredUncompressed > MaxUncompressedArchiveBytes)
        {
            return Task.FromResult(new SourceReadResult
            {
                Errors = [$"Archive expands to {declaredUncompressed / (1024 * 1024)} MB, over the {MaxUncompressedArchiveBytes / (1024 * 1024)} MB limit"],
                OnDispose = cleanup
            });
        }

        _logStore.AddEntry(feedVersionId, "Validating GTFS files...");
        var validation = _gtfs.ValidateGtfs(archive);
        if (!validation.IsValid)
        {
            return Task.FromResult(new SourceReadResult
            {
                Errors = ["GTFS validation failed: " + string.Join("; ", validation.Errors)],
                OnDispose = cleanup
            });
        }
        _logStore.AddEntry(feedVersionId, "Validation passed");

        _logStore.AddEntry(feedVersionId, "Parsing GTFS files...");
        var agencies = _gtfs.ParseAgencies(archive);
        var (stops, droppedStops) = _gtfs.ParseStops(archive);
        if (droppedStops > 0)
            _logStore.AddEntry(feedVersionId, $"Skipped {droppedStops} stop(s) with invalid coordinates");
        var routes = _gtfs.ParseRoutes(archive);
        var trips = _gtfs.ParseTrips(archive);
        var calendar = _gtfs.ParseCalendar(archive);
        var calendarDates = _gtfs.ParseCalendarDates(archive);
        var shapes = _gtfs.ParseShapes(archive);

        if (calendar.Count == 0 && calendarDates.Count == 0)
        {
            return Task.FromResult(new SourceReadResult
            {
                Errors = ["Feed has no calendar or calendar_dates data — zero valid services. No departures will appear for any stop."],
                OnDispose = cleanup
            });
        }

        var document = new TransitDocument
        {
            OperatorId = feed.OperatorId,
            Agencies = agencies,
            Stops = stops,
            Routes = routes,
            Trips = trips,
            Calendar = calendar,
            CalendarDates = calendarDates,
            Shapes = shapes,
            HasStopTimes = true
        };

        _logStore.AddEntry(feedVersionId, $"Parsed: {document.Summary()}");

        return Task.FromResult(new SourceReadResult
        {
            Document = document,
            StopTimes = () => _gtfs.ParseStopTimesBatchedAsync(archive, 50000),
            OnDispose = cleanup
        });
    }

    private Func<ValueTask> Cleanup(ZipArchive archive, string tempZipPath) => () =>
    {
        // Order matters on Windows: the file cannot be deleted while the archive still holds it.
        try { archive.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to dispose GTFS archive"); }
        TryDelete(tempZipPath);
        return ValueTask.CompletedTask;
    };

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp file {Path}", path);
        }
    }
}
