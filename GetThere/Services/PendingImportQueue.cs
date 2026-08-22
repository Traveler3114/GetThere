using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using GetThereShared.Contracts;

namespace GetThere.Services;

/// <summary>
/// One queued import plus the owner it was created under.
/// <para>
/// The hash is of <c>AuthService.GetOwnerKeyAsync()</c>, using the same construction
/// <c>TicketStore.DirectoryFor</c> uses, so the two stores agree on who someone is. A null hash is
/// an entry written before this field existed — treated as "owner unknown", which prompts.
/// </para>
/// </summary>
public sealed record QueuedImport(CreateImportedTicketRequest Request, string? OwnerHash);

/// <summary>
/// Imports created on the device that the server has not accepted yet.
/// <para>
/// A ticket is saved locally first and pushed afterwards, so importing works signed out and with no
/// signal. This holds the ones still owed to the server: everything created by a guest, and anything
/// created by a signed-in user while the network was down.
/// </para>
/// <para>
/// Deliberately a queue and not a sync engine. Entries only ever flow one way — device to server —
/// and are dropped once accepted. There is no conflict resolution here because there is no conflict:
/// nothing else can have edited a ticket the server has never seen.
/// </para>
/// <para>
/// <b>2. Plaintext.</b> <see cref="TicketStore"/> encrypts with AES-GCM under a key in
/// <c>SecureStorage</c>, on the stated grounds that "a ticket payload is a bearer credential for
/// travel: whoever renders it rides", and that <c>AppDataDirectory</c> is app-private but not proof
/// against a rooted device or an ADB backup. This file holds the identical
/// <see cref="CreateImportedTicketRequest"/> payloads as readable JSON. It is also the copy that
/// persists longest: a guest never signs in, so nothing ever drains their queue.
/// </para>
/// <para>
/// The least-protected store therefore holds the credential for the longest, and the encrypted one
/// holds only the copy the server already has. Unlike the first gap this one has no trade-off —
/// it wants the same cipher, which means lifting <c>TicketStore</c>'s key handling somewhere both
/// can reach.
/// </para>
/// </summary>
public sealed class PendingImportQueue : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path = Path.Combine(FileSystem.AppDataDirectory, "pending-imports.json");

    /// <summary>Hashes an owner key the same way <c>TicketStore</c> does, so both agree on identity.</summary>
    public static string HashOwner(string ownerKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerKey)))[..32];

    /// <summary>
    /// Adds a ticket to the queue.
    /// <para>
    /// The request carries a <c>ClientId</c> minted when the ticket was created, not when it is
    /// pushed. That is what makes a replay safe: the id survives the app being killed mid-push, and
    /// the server returns the original rather than inserting a second copy.
    /// </para>
    /// </summary>
    public async Task EnqueueAsync(CreateImportedTicketRequest request, string? ownerHash)
    {
        await _lock.WaitAsync();
        try
        {
            var pending = await ReadUnlockedAsync();
            pending.RemoveAll(p => p.Request.ClientId == request.ClientId);
            pending.Add(new QueuedImport(request, ownerHash));
            await WriteUnlockedAsync(pending);
        }
        finally { _lock.Release(); }
    }

    public async Task<List<QueuedImport>> PeekAllAsync()
    {
        await _lock.WaitAsync();
        try { return await ReadUnlockedAsync(); }
        finally { _lock.Release(); }
    }

    /// <summary>Drops one entry, once the server has confirmed it.</summary>
    public async Task RemoveAsync(Guid? clientId)
    {
        if (clientId is null) return;

        await _lock.WaitAsync();
        try
        {
            var pending = await ReadUnlockedAsync();
            if (pending.RemoveAll(p => p.Request.ClientId == clientId) > 0)
                await WriteUnlockedAsync(pending);
        }
        finally { _lock.Release(); }
    }

    public async Task<int> CountAsync() => (await PeekAllAsync()).Count;

    /// <summary>Entries whose owner differs from <paramref name="ownerHash"/>, or whose owner is unknown.</summary>
    public async Task<List<QueuedImport>> PeekForeignAsync(string ownerHash)
    {
        await _lock.WaitAsync();
        try
        {
            var pending = await ReadUnlockedAsync();
            return pending.Where(p => p.OwnerHash is null || !string.Equals(p.OwnerHash, ownerHash, StringComparison.Ordinal)).ToList();
        }
        finally { _lock.Release(); }
    }

    /// <summary>Re-stamps unowned or foreign entries onto <paramref name="ownerHash"/>, after the user agrees.</summary>
    public async Task AdoptAsync(string ownerHash)
    {
        await _lock.WaitAsync();
        try
        {
            var pending = await ReadUnlockedAsync();
            var changed = false;
            for (var i = 0; i < pending.Count; i++)
            {
                if (pending[i].OwnerHash is null || !string.Equals(pending[i].OwnerHash, ownerHash, StringComparison.Ordinal))
                {
                    pending[i] = pending[i] with { OwnerHash = ownerHash };
                    changed = true;
                }
            }
            if (changed)
                await WriteUnlockedAsync(pending);
        }
        finally { _lock.Release(); }
    }

    private async Task<List<QueuedImport>> ReadUnlockedAsync()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var json = await File.ReadAllTextAsync(_path);
            var asQueued = JsonSerializer.Deserialize<List<QueuedImport>>(json, JsonOptions);
            if (asQueued is not null)
            {
                if (asQueued.Count == 0) return asQueued;
                if (asQueued.Any(q => q.Request is null))
                {
                    var asOld = JsonSerializer.Deserialize<List<CreateImportedTicketRequest>>(json, JsonOptions);
                    if (asOld is not null)
                        return asOld.Select(r => new QueuedImport(r, null)).ToList();
                }
                else
                {
                    return asQueued;
                }
            }

            var fallback = JsonSerializer.Deserialize<List<CreateImportedTicketRequest>>(json, JsonOptions);
            if (fallback is not null)
                return fallback.Select(r => new QueuedImport(r, null)).ToList();

            return asQueued ?? [];
        }
        catch (Exception ex)
        {
            // An unreadable queue must not stop the app importing. The tickets themselves are in
            // TicketStore either way — this file only decides what still owes the server.
            Trace.WriteLine($"[PendingImportQueue] Could not read the queue: {ex.Message}");
            return [];
        }
    }

    private async Task WriteUnlockedAsync(List<QueuedImport> pending)
    {
        try
        {
            // Same temp-then-move as TicketStore: a process killed mid-write would otherwise leave a
            // truncated file, and the queue would read as empty — silently losing tickets the server
            // has never seen, which is the one thing here that cannot be recovered from anywhere.
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(pending, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PendingImportQueue] Could not write the queue: {ex.Message}");
        }
    }

    /// <summary>
    /// Releases the lock. A singleton for the app's lifetime, so this runs only when the DI container
    /// is torn down at shutdown.
    /// </summary>
    public void Dispose() => _lock.Dispose();
}
