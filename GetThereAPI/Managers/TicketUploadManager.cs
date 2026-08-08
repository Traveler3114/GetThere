using GetThereAPI.Common;
using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Exceptions;
using GetThereAPI.Services;

using GetThereShared.Contracts;
using GetThereShared.Enums;
using GetThereShared.Extraction;

using Microsoft.EntityFrameworkCore;

namespace GetThereAPI.Managers;

/// <summary>
/// Accepts a ticket file, decides what it actually is, extracts what it can, and stores it against
/// a single-use key the user can spend on one imported ticket.
/// </summary>
public class TicketUploadManager
{
    private readonly AppDbContext _db;
    private readonly ITicketFileStore _store;
    private readonly ITicketFileScanner _scanner;
    private readonly TicketExtractorRegistry _extractors;
    private readonly ILogger<TicketUploadManager> _logger;

    public TicketUploadManager(
        AppDbContext db,
        ITicketFileStore store,
        ITicketFileScanner scanner,
        TicketExtractorRegistry extractors,
        ILogger<TicketUploadManager> logger)
    {
        _db = db;
        _store = store;
        _scanner = scanner;
        _extractors = extractors;
        _logger = logger;
    }

    /// <summary>Ceiling on an uploaded ticket file, per the agreed storage strategy.</summary>
    public const long MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>An upload the user never turned into a ticket is swept after this long.</summary>
    public static readonly TimeSpan UnconsumedRetention = TimeSpan.FromHours(24);

    public async Task<TicketUploadResponse> UploadAsync(string userId, Stream stream, long declaredLength, CancellationToken ct = default)
    {
        // Checked before reading as well as by the framework's request-size limit, because
        // declaredLength is a header the caller controls and the copy below is what actually
        // spends memory.
        if (declaredLength > MaxFileBytes)
            throw new AppException($"That file is larger than the {MaxFileBytes / 1024 / 1024} MB limit.", 400);

        var content = await ReadBoundedAsync(stream, ct);
        if (content.Length == 0)
            throw new AppException("That file is empty.", 400);

        // The declared Content-Type and the filename are both caller-chosen, so neither decides
        // what gets opened. The sniffed type is the allow-list and the extractor selector.
        var fileType = TicketFileSniffer.Detect(content.AsSpan(0, Math.Min(TicketFileSniffer.HeaderBytes, content.Length)));
        if (fileType is null)
            throw new AppException($"That file type is not supported. Upload a {TicketFileSniffer.SupportedDescription}.", 400);

        if (!await _scanner.IsSafeAsync(content, ct))
        {
            _logger.LogWarning("Ticket upload rejected by scanner for user {UserId}", userId);
            throw new AppException("That file was rejected by a security scan.", 400);
        }

        var extractor = _extractors.For(fileType.Value)
            ?? throw new AppException($"That file type is not supported. Upload a {TicketFileSniffer.SupportedDescription}.", 400);

        // Extraction runs before the file is stored, so a file we cannot read never becomes an
        // orphaned blob with a key nobody can spend.
        var extraction = await extractor.ExtractAsync(content, fileType.Value, ct);

        var contentType = TicketFileSniffer.ContentTypeFor(fileType.Value);
        var blobKey = await _store.SaveAsync(userId, content, TicketFileSniffer.ExtensionFor(fileType.Value), ct);

        _db.TicketUploads.Add(new TicketUpload
        {
            UserId = userId,
            BlobKey = blobKey,
            FileType = fileType.Value,
            ContentType = contentType,
            SizeBytes = content.Length,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} uploaded a {FileType} ticket file ({Bytes} bytes), detected {Count} fields",
            userId, fileType, content.Length, extraction.DetectedFields.Count);

        return new TicketUploadResponse
        {
            BlobKey = blobKey,
            FileType = fileType.Value,
            ContentType = contentType,
            SizeBytes = content.Length,
            Extraction = extraction
        };
    }

    /// <summary>Extraction over pasted text, which has no file and so needs no storage.</summary>
    public static TicketExtractionResult ExtractText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new AppException("There was no text to read.", 400);

        var result = new TicketExtractionResult();
        TicketTextScraper.Scrape(text, result);

        if (result.DetectedFields.Count == 0)
            result.Warning = "We could not read any ticket details from that text — fill them in below.";

        return result;
    }

    /// <summary>Returns the stored file for a ticket the caller owns.</summary>
    public async Task<(byte[] Content, string ContentType)?> GetFileAsync(string userId, int ticketId, CancellationToken ct = default)
    {
        var ticket = await _db.ImportedTickets
            .Where(t => t.Id == ticketId && t.UserId == userId)
            .Select(t => new { t.SourceFileBlobKey, t.SourceFileContentType })
            .FirstOrDefaultAsync(ct);

        if (ticket?.SourceFileBlobKey is null) return null;

        var content = await _store.ReadAsync(userId, ticket.SourceFileBlobKey, ct);
        if (content is null) return null;

        return (content, ticket.SourceFileContentType ?? "application/octet-stream");
    }

    /// <summary>
    /// Deletes uploads the user never turned into a ticket. Called by the expiry worker; abandoned
    /// files would otherwise accumulate on disk forever, since nothing else references them.
    /// </summary>
    public async Task<int> PurgeAbandonedAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - UnconsumedRetention;
        var abandoned = await _db.TicketUploads
            .Where(u => u.ConsumedAt == null && u.CreatedAt < cutoff)
            .ToListAsync(ct);

        // Only rows whose blob actually went away are removed. Dropping the row regardless — which
        // is what this did — left the file on disk with nothing left in the database pointing at it,
        // so it could never be found or retried: the opposite of what the sweep is for. A row whose
        // delete failed stays put and is picked up by the next pass.
        List<Entities.TicketUpload> deleted = [];

        foreach (var upload in abandoned)
        {
            try
            {
                await _store.DeleteAsync(upload.UserId, upload.BlobKey, ct);
                deleted.Add(upload);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Logged and left for the next sweep rather than retried here, so one unhappy blob
                // does not stall the rest of the backlog.
                _logger.LogWarning(ex, "Could not delete abandoned ticket file {BlobKey}; leaving the row for the next sweep", upload.BlobKey);
            }
        }

        _db.TicketUploads.RemoveRange(deleted);
        await _db.SaveChangesAsync(ct);
        return deleted.Count;
    }

    /// <summary>
    /// Copies at most <see cref="MaxFileBytes"/>, then checks whether anything remains. A stream
    /// that under-reports its length cannot get more than one byte past the ceiling.
    /// </summary>
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;

        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > MaxFileBytes)
                throw new AppException($"That file is larger than the {MaxFileBytes / 1024 / 1024} MB limit.", 400);
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
