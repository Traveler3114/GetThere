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
        Path.Combine(DirectoryFor(ownerKey), name + ".bin");

    // ── Encryption ───────────────────────────────────────────────────────────────
    // A ticket payload is a bearer credential for travel: whoever renders it rides. AppDataDirectory
    // is app-private, which is enough against other apps but not against a rooted or jailbroken
    // device, or an ADB backup. Encrypting with a key kept in SecureStorage — the same store already
    // holding the auth tokens — puts these at parity with the credentials they are equivalent to,
    // and costs a key unwrap per read.

    private const string EncryptionKeyName = "ticket_cache_key";
    private const int NonceLength = 12;   // AES-GCM standard, and what the class requires.
    private const int TagLength = 16;

    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private byte[]? _cachedKey;

    /// <summary>
    /// Fetches the cache key, generating one on first use.
    /// <para>
    /// Behind a lock because two collections are written back to back: without it both could find no
    /// key, generate different ones, and the second would overwrite the first — leaving the earlier
    /// file undecryptable.
    /// </para>
    /// </summary>
    private async Task<byte[]> GetKeyAsync()
    {
        if (_cachedKey is not null) return _cachedKey;

        await _keyLock.WaitAsync();
        try
        {
            if (_cachedKey is not null) return _cachedKey;

            var stored = await SecureStorage.Default.GetAsync(EncryptionKeyName);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                _cachedKey = Convert.FromBase64String(stored);
                return _cachedKey;
            }

            var key = RandomNumberGenerator.GetBytes(32);
            await SecureStorage.Default.SetAsync(EncryptionKeyName, Convert.ToBase64String(key));
            _cachedKey = key;
            return key;
        }
        finally
        {
            _keyLock.Release();
        }
    }

    /// <summary>Nonce ‖ tag ‖ ciphertext. The nonce is fresh per write and never reused.</summary>
    private static byte[] Encrypt(byte[] key, string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagLength];

        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[NonceLength + TagLength + cipher.Length];
        nonce.CopyTo(output, 0);
        tag.CopyTo(output, NonceLength);
        cipher.CopyTo(output, NonceLength + TagLength);
        return output;
    }

    /// <summary>
    /// Reverses <see cref="Encrypt"/>. Throws on a tampered or truncated file, which the caller
    /// treats the same as a missing one.
    /// </summary>
    private static string Decrypt(byte[] key, byte[] payload)
    {
        if (payload.Length < NonceLength + TagLength)
            throw new CryptographicException("Cached file is too short to be valid.");

        var nonce = payload.AsSpan(0, NonceLength);
        var tag = payload.AsSpan(NonceLength, TagLength);
        var cipher = payload.AsSpan(NonceLength + TagLength);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagLength);

        // Authenticated: a file edited on a rooted device fails here rather than deserialising into
        // a ticket someone chose the contents of.
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

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

            var key = await GetKeyAsync();
            var sealed_ = Encrypt(key, JsonSerializer.Serialize(payload, JsonOptions));

            await File.WriteAllBytesAsync(tempPath, sealed_);
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

    private async Task<CachedTickets<T>?> ReadAsync<T>(string ownerKey, string name)
    {
        try
        {
            var path = PathFor(ownerKey, name);
            if (!File.Exists(path)) return null;

            var key = await GetKeyAsync();
            var json = Decrypt(key, await File.ReadAllBytesAsync(path));
            return JsonSerializer.Deserialize<CachedTickets<T>>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            // Corrupt, tampered with, or written under a key that no longer exists — all the same as
            // absent to the caller, which shows its ordinary offline state rather than a second,
            // stranger error. A failed authentication tag lands here too, which is the point of
            // using an authenticated cipher.
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
