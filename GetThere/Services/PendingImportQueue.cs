using System.Diagnostics;
using System.Text.Json;

using GetThereShared.Contracts;

namespace GetThere.Services;

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
/// </summary>
public class PendingImportQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path = Path.Combine(FileSystem.AppDataDirectory, "pending-imports.json");

    /// <summary>
    /// Adds a ticket to the queue.
    /// <para>
    /// The request carries a <c>ClientId</c> minted when the ticket was created, not when it is
    /// pushed. That is what makes a replay safe: the id survives the app being killed mid-push, and
    /// the server returns the original rather than inserting a second copy.
    /// </para>
    /// </summary>
    public async Task EnqueueAsync(CreateImportedTicketRequest request)
    {
        await _lock.WaitAsync();
        try
        {
            var pending = await ReadUnlockedAsync();
            pending.RemoveAll(p => p.ClientId == request.ClientId);
            pending.Add(request);
            await WriteUnlockedAsync(pending);
        }
        finally { _lock.Release(); }
    }

    public async Task<List<CreateImportedTicketRequest>> PeekAllAsync()
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
            if (pending.RemoveAll(p => p.ClientId == clientId) > 0)
                await WriteUnlockedAsync(pending);
        }
        finally { _lock.Release(); }
    }

    public async Task<int> CountAsync() => (await PeekAllAsync()).Count;

    private async Task<List<CreateImportedTicketRequest>> ReadUnlockedAsync()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var json = await File.ReadAllTextAsync(_path);
            return JsonSerializer.Deserialize<List<CreateImportedTicketRequest>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            // An unreadable queue must not stop the app importing. The tickets themselves are in
            // TicketStore either way — this file only decides what still owes the server.
            Trace.WriteLine($"[PendingImportQueue] Could not read the queue: {ex.Message}");
            return [];
        }
    }

    private async Task WriteUnlockedAsync(List<CreateImportedTicketRequest> pending)
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
}
