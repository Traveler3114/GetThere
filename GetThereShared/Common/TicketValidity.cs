namespace GetThereShared.Common;

/// <summary>
/// Whether a ticket's validity window has closed, for **display only**.
/// <para>
/// Status is the server's to decide. <c>TicketExpiryWorker</c> sweeps hourly and writes `Expired`
/// into the database; nothing else may. This exists because a client showing a cached ticket has a
/// status that was true when it was cached and may not be now — and telling a traveller their ticket
/// is `Active` when its window shut two hours ago is the one failure that matters at a barrier.
/// </para>
/// <para>
/// The result must never be written into a status field, sent to the server, or used to decide what
/// an API call may do. It changes what a screen says and nothing else.
/// </para>
/// </summary>
public static class TicketValidity
{
    /// <summary>
    /// True when the ticket is still recorded as active but its window has already closed.
    /// </summary>
    /// <param name="isActive">Whether the stored status is the active one for its ticket type.</param>
    /// <param name="validTo">End of the validity window, if the ticket has one.</param>
    /// <param name="nowUtc">Current time, in UTC. Passed in so this stays testable.</param>
    /// <remarks>
    /// <para>
    /// <b>Only ever downgrades.</b> A ticket already recorded as `Used`, `Cancelled` or `Expired`
    /// returns false, because those are terminal and unknowable offline: they come from an explicit
    /// action, possibly on another device. Recomputing them would resurrect a cancelled ticket to
    /// active, which at a barrier looks like fare evasion.
    /// </para>
    /// <para>
    /// <b>A null window never expires</b>, matching the server: the expiry sweep requires
    /// <c>ValidTo</c> to be non-null, and a SQL comparison against NULL is never true either. An
    /// open-ended ticket stays active until something says otherwise.
    /// </para>
    /// <para>
    /// <b>UTC, always.</b> The worker compares against <c>DateTime.UtcNow</c>, and a timestamp
    /// deserialised from JSON can arrive as <see cref="DateTimeKind.Unspecified"/> — comparing that
    /// to local time is wrong by the device's offset. This exact confusion has already caused a bug
    /// on the write path, which is why <c>ValidTo</c> is normalised here rather than trusted.
    /// </para>
    /// </remarks>
    public static bool IsPastValidity(bool isActive, DateTime? validTo, DateTime nowUtc)
    {
        if (!isActive) return false;
        if (validTo is not { } end) return false;

        // Unspecified means "came off the wire without a kind". The server stores and compares UTC,
        // so that is what an unmarked value is; treating it as local would shift it by the device's
        // offset and expire tickets early or late depending on where the user is standing.
        var endUtc = end.Kind switch
        {
            DateTimeKind.Utc => end,
            DateTimeKind.Local => end.ToUniversalTime(),
            _ => DateTime.SpecifyKind(end, DateTimeKind.Utc)
        };

        return endUtc < nowUtc;
    }
}
