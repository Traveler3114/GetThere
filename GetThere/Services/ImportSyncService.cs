using System.Diagnostics;

using GetThereShared.Contracts;

namespace GetThere.Services;

/// <summary>
/// Pushes locally-created imports to the server once there is an account and a connection.
/// <para>
/// This is the guest-to-account upgrade. Nothing did it before: <c>ClearGuest()</c> ran on logout and
/// never on register or login, and no data moved anywhere — so a guest who imported tickets and then
/// signed up would have found their wallet empty, which is a worse outcome than not letting them
/// import at all.
/// </para>
/// </summary>
public class ImportSyncService
{
    private readonly ImportedTicketService _importedService;
    private readonly PendingImportQueue _queue;
    private readonly AuthService _authService;

    public ImportSyncService(
        ImportedTicketService importedService,
        PendingImportQueue queue,
        AuthService authService)
    {
        _importedService = importedService;
        _queue = queue;
        _authService = authService;
    }

    /// <summary>
    /// Drains the queue, best-effort.
    /// </summary>
    /// <returns>How many tickets the server accepted.</returns>
    /// <remarks>
    /// <para>
    /// Safe to call repeatedly and safe to interrupt. Each entry carries a <c>ClientId</c>, so a
    /// push that succeeded on the server but never reached the client — killed app, dropped
    /// response — is answered on the next attempt with the original ticket rather than a duplicate.
    /// </para>
    /// <para>
    /// A failure stops the drain rather than skipping the entry. The usual cause is the network
    /// being gone, and hammering the rest of the queue against a dead connection only burns battery;
    /// the next call picks up where this one stopped.
    /// </para>
    /// </remarks>
    public async Task<int> FlushAsync()
    {
        if (!await _authService.IsLoggedInAsync()) return 0;

        var pending = await _queue.PeekAllAsync();
        if (pending.Count == 0) return 0;

        var pushed = 0;

        foreach (var request in pending)
        {
            try
            {
                var result = await _importedService.CreateAsync(request);

                if (result.Success)
                {
                    await _queue.RemoveAsync(request.ClientId);
                    pushed++;
                    continue;
                }

                // A duplicate means the server already holds this ticket — by hash, from an earlier
                // push under a different client id, or from another device. Either way it is no
                // longer owed, and leaving it queued would retry forever.
                if (string.Equals(result.Code, ImportedTicketService.DuplicateCode, StringComparison.Ordinal))
                {
                    Trace.WriteLine($"[ImportSync] Dropping {request.ClientId}: the server already has it.");
                    await _queue.RemoveAsync(request.ClientId);
                    continue;
                }

                Trace.WriteLine($"[ImportSync] Stopping: {result.Message}");
                break;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ImportSync] Stopping: {ex.Message}");
                break;
            }
        }

        return pushed;
    }
}
