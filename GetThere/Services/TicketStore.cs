using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using GetThereShared.Contracts;

namespace GetThere.Services;

/// <summary>A cached collection plus when it was written.</summary>
/// <typeparam name="T">The contract stored.</typeparam>
public sealed class CachedTickets<T>
{
    public List<T> Items { get; set; } = [];

    /// <summary>When this was last written. Surfaced to the user, so it is UTC and never local.</summary>
    public DateTime CachedAtUtc { get; set; }
}

/// <summary>
/// Keeps the user's tickets on the device so they survive having no signal.
/// <para>
/// The client persisted nothing before this — no SQLite, no file cache, <c>AppDataDirectory</c>
/// unused, every screen a live HTTP read on appear. Offline meant an empty list and an error label,
/// which for a travel wallet is exactly backwards: a ticket is most needed at a barrier, which is
/// where signal is worst.
/// </para>
/// <para>
/// Deliberately not a sync engine. Writes happen as a by-product of a successful online read, and
/// reads happen only when the live call fails, so a bug here cannot serve a stale ticket to someone
/// who is online. Files and JSON rather than a database: the volumes are tens of rows, and a package
/// plus a schema plus migrations is a large first step for a list.
/// </para>
/// </summary>
public class TicketStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Serialises writes. Two screens can finish loading at once, and a half-written file is worse
    /// than none — it deserialises to garbage rather than failing cleanly.
    /// </summary>
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly string _root = Path.Combine(FileSystem.AppDataDirectory, "tickets");

    /// <summary>
    /// Per-owner directory.
    /// <para>
    /// Keyed by owner because a device is not a person: two accounts, or an account and the guest
    /// who used the phone before them, must never see each other's tickets. The owner key is hashed
    /// rather than used raw — it is a user id, and putting identifiers in path names leaks them to
    /// anything that can list the directory, as well as risking characters a file system rejects.
    /// </para>
    /// </summary>
    private string DirectoryFor(string ownerKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerKey)))[..32];
        return Path.Combine(_root, hash);
    }

    private string PathFor(string ownerKey, string name) =>
        Path.Combine(DirectoryFor(ownerKey), name + ".json");

    private const string ImportedFile = "imported";
    private const string PurchasedFile = "purchased";

    public Task SaveImportedAsync(string ownerKey, IEnumerable<ImportedTicketResponse> tickets) =>
        SaveAsync(ownerKey, ImportedFile, tickets);

    public Task SavePurchasedAsync(string ownerKey, IEnumerable<TicketResponse> tickets) =>
        SaveAsync(ownerKey, PurchasedFile, tickets);

    public Task<CachedTickets<ImportedTicketResponse>?> ReadImportedAsync(string ownerKey) =>
        ReadAsync<ImportedTicketResponse>(ownerKey, ImportedFile);

    public Task<CachedTickets<TicketResponse>?> ReadPurchasedAsync(string ownerKey) =>
        ReadAsync<TicketResponse>(ownerKey, PurchasedFile);

    private async Task SaveAsync<T>(string ownerKey, string name, IEnumerable<T> tickets)
    {
        await _writeLock.WaitAsync();
        try
        {
            var directory = DirectoryFor(ownerKey);
            Directory.CreateDirectory(directory);

            var payload = new CachedTickets<T>
            {
                Items = tickets.ToList(),
                CachedAtUtc = DateTime.UtcNow
            };

            // Write beside the target and move into place. A process killed mid-write — which on a
            // phone is routine, not exceptional — would otherwise leave a truncated file that reads
            // as corrupt on next launch, losing the cache exactly when it was about to be needed.
            var finalPath = PathFor(ownerKey, name);
            var tempPath = finalPath + ".tmp";

            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(payload, JsonOptions));
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch (Exception ex)
        {
            // A cache that cannot be written must not break the screen that was merely trying to
            // populate it. The read path treats a missing file as "no cache", which is correct.
            Trace.WriteLine($"[TicketStore] Could not cache {name}: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task<CachedTickets<T>?> ReadAsync<T>(string ownerKey, string name)
    {
        try
        {
            var path = PathFor(ownerKey, name);
            if (!File.Exists(path)) return null;

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<CachedTickets<T>>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            // Corrupt or unreadable is the same as absent as far as the caller is concerned: it
            // shows its ordinary offline state rather than a second, stranger error.
            Trace.WriteLine($"[TicketStore] Could not read cached {name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Drops everything held for one owner. Used on an explicit sign-out.
    /// </summary>
    /// <remarks>
    /// Deliberately *not* called from the 401 path in <c>AuthenticatedHttpHandler</c>. That fires
    /// when a refresh is rejected, which is not always a decision the user made, and wiping their
    /// tickets in response would take the cache away in exactly the situation it exists for.
    /// </remarks>
    public void Clear(string ownerKey)
    {
        try
        {
            var directory = DirectoryFor(ownerKey);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[TicketStore] Could not clear the cache: {ex.Message}");
        }
    }
}
