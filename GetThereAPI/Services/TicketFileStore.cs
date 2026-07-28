using GetThereAPI.Exceptions;

namespace GetThereAPI.Services;

/// <summary>
/// Where uploaded ticket files live. Implementations receive a blob key that the store itself
/// minted — callers never choose one, and no caller-supplied filename reaches the backing medium.
/// </summary>
public interface ITicketFileStore
{
    /// <summary>Mints a key and writes the bytes. The returned key is the only handle to the file.</summary>
    Task<string> SaveAsync(string userId, byte[] content, string extension, CancellationToken ct = default);

    /// <summary>Reads a previously stored file, or null when the key resolves to nothing.</summary>
    Task<byte[]?> ReadAsync(string userId, string blobKey, CancellationToken ct = default);

    Task DeleteAsync(string userId, string blobKey, CancellationToken ct = default);
}

/// <summary>
/// Local-disk store, rooted under the content root. This deployment has no cloud storage of any
/// kind — no Azure, no S3, no credentials — and <c>TransitInfoAPI</c> already keeps GTFS feeds on
/// disk the same way. Swapping in object storage later means replacing this one class.
/// </summary>
public class LocalTicketFileStore : ITicketFileStore
{
    private readonly string _root;
    private readonly ILogger<LocalTicketFileStore> _logger;

    public LocalTicketFileStore(IWebHostEnvironment env, IConfiguration configuration, ILogger<LocalTicketFileStore> logger)
    {
        var configured = configuration.GetValue<string>("TicketFiles:RootPath");
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "ticket-files")
            : configured);
        _logger = logger;
    }

    public async Task<string> SaveAsync(string userId, byte[] content, string extension, CancellationToken ct = default)
    {
        // Server-minted, so the key carries no caller input at all. The extension comes from the
        // sniffed type, not from the uploaded filename.
        var blobKey = $"{Guid.NewGuid():N}{extension}";
        var path = ResolvePath(userId, blobKey);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temp name and move, so a failed or cancelled write cannot leave a truncated
        // file sitting under a key the database already believes in.
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, content, ct);
        File.Move(tmp, path, overwrite: true);

        _logger.LogInformation("Stored ticket file {BlobKey} ({Bytes} bytes) for user {UserId}", blobKey, content.Length, userId);
        return blobKey;
    }

    public async Task<byte[]?> ReadAsync(string userId, string blobKey, CancellationToken ct = default)
    {
        var path = ResolvePath(userId, blobKey);
        if (!File.Exists(path)) return null;
        return await File.ReadAllBytesAsync(path, ct);
    }

    public Task DeleteAsync(string userId, string blobKey, CancellationToken ct = default)
    {
        var path = ResolvePath(userId, blobKey);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a user's blob to an absolute path and refuses anything that escapes the storage
    /// root. Keys are server-minted, but this is the same containment check
    /// <c>FeedManager.GetFeedStorageDirectory</c> applies to feed ids — a previous path-traversal
    /// defect in that code came from trusting an identifier that "could not" contain separators.
    /// </summary>
    internal string ResolvePath(string userId, string blobKey)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(blobKey))
            throw new AppException("Invalid ticket file reference.", 400);

        // Checked as a string before it is ever treated as a path, because what counts as a
        // separator is platform-dependent: a key of "..\..\etc\passwd" is an inert filename on
        // Linux but escapes on Windows. Rejecting both separators outright means the guard does
        // not change meaning with the deployment target.
        EnsurePlainFileName(userId);
        EnsurePlainFileName(blobKey);

        var resolved = Path.GetFullPath(Path.Combine(_root, userId, blobKey));
        var userRoot = Path.GetFullPath(Path.Combine(_root, userId));

        // Belt and braces: even with the shape check above, the resolved path must land inside the
        // user's own directory. This mirrors FeedManager.GetFeedStorageDirectory.
        if (!resolved.StartsWith(userRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new AppException("Invalid ticket file reference.", 400);

        return resolved;
    }

    private static void EnsurePlainFileName(string value)
    {
        if (value.Contains('/', StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains('\0', StringComparison.Ordinal)
            || Path.IsPathRooted(value))
        {
            throw new AppException("Invalid ticket file reference.", 400);
        }
    }
}

/// <summary>
/// Malware scanning hook. The storage decision reserved an extension point for this; wiring the
/// no-op in now means adding a real scanner is a DI registration rather than a change to the
/// upload path.
/// </summary>
public interface ITicketFileScanner
{
    /// <summary>Returns false to reject the upload.</summary>
    Task<bool> IsSafeAsync(byte[] content, CancellationToken ct = default);
}

/// <summary>Accepts everything. Replace the registration to enforce real scanning.</summary>
public class NoOpTicketFileScanner : ITicketFileScanner
{
    public Task<bool> IsSafeAsync(byte[] content, CancellationToken ct = default) => Task.FromResult(true);
}
