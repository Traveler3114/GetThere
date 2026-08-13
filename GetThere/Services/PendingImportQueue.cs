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
/// <para>
/// <b>Two gaps against <see cref="TicketStore"/>, which holds the same payloads.</b> Recorded here
/// rather than fixed because neither can be verified in the environment this was audited from — the
/// MAUI head cannot be built or run there — and the first of the two is a product decision rather
/// than a defect with one right answer.
/// </para>
/// <para>
/// <b>1. No owner.</b> <see cref="TicketStore"/> keys every file by a hash of the owner, and says
/// why: "a device is not a person: two accounts, or an account and the guest who used the phone
/// before them, must never see each other's tickets." This queue is one global file with no owner
/// recorded anywhere, and <c>ImportSyncService.FlushAsync</c> gates only on
/// <c>IsLoggedInAsync()</c> — it cannot check whose entries these are, because none of them say.
/// So: a guest imports a ticket on a shared or second-hand phone; a different person signs in;
/// <c>TicketsViewModel.LoadTickets</c> calls <c>FlushAsync</c>, which finds a logged-in user and
/// pushes the first person's ticket — barcode payload included — into the second person's account.
/// </para>
/// <para>
/// What makes this a decision and not a bug fix: entries created by a guest are <em>supposed</em>
/// to migrate to whoever signs in next. That is the guest-to-account upgrade this whole path exists
/// for, and the app has no way to know whether the guest and the new account are the same person.
/// Recording the owner would fix the signed-in-user case cleanly; the guest case needs someone to
/// decide whether an unclaimed ticket follows the next sign-in or is discarded at the account
/// boundary.
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

    /// <summary>
    /// Releases the lock. A singleton for the app's lifetime, so this runs only when the DI container
    /// is torn down at shutdown.
    /// </summary>
    public void Dispose() => _lock.Dispose();
}
