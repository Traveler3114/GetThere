using GetThereShared.Common;

namespace GetThere.Tests.Tickets;

/// <summary>
/// The display-only past-validity rule.
/// <para>
/// The consequence of getting this wrong is asymmetric. Showing an expired ticket as active is the
/// failure that matters — a traveller holds it up and it does not work. Showing an active one as
/// expired is merely alarming. So the rule only ever downgrades, and every case below is really
/// about proving it cannot do the opposite.
/// </para>
/// </summary>
public class TicketValidityTests
{
    private static readonly DateTime Now = new(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_active_ticket_whose_window_has_closed_reads_as_past_validity()
    {
        // The case the rule exists for: the server's sweep runs hourly, so this ticket still says
        // Active in the database and would say Active on screen without this.
        Assert.True(TicketValidity.IsPastValidity(isActive: true, Now.AddMinutes(-10), Now));
    }

    [Fact]
    public void An_active_ticket_still_inside_its_window_is_not()
    {
        Assert.False(TicketValidity.IsPastValidity(isActive: true, Now.AddHours(1), Now));
    }

    // ── Only ever downgrade ──────────────────────────────────────────────────────
    // Used and Cancelled come from an explicit action, possibly on another device, and are
    // unknowable offline. A rule that recomputed them would resurrect a cancelled ticket to active,
    // which at a barrier looks like fare evasion.

    [Fact]
    public void A_terminal_ticket_is_never_reconsidered_even_with_a_future_window()
    {
        // isActive false means the stored status is Used, Cancelled or Expired. Whatever the dates
        // say, the answer is no.
        Assert.False(TicketValidity.IsPastValidity(isActive: false, Now.AddYears(1), Now));
        Assert.False(TicketValidity.IsPastValidity(isActive: false, Now.AddYears(-1), Now));
        Assert.False(TicketValidity.IsPastValidity(isActive: false, null, Now));
    }

    [Fact]
    public void A_ticket_with_no_end_date_never_expires()
    {
        // Matches the server: the expiry sweep requires ValidTo to be non-null, and a SQL comparison
        // against NULL is never true. An open-ended ticket stays active until something says
        // otherwise.
        Assert.False(TicketValidity.IsPastValidity(isActive: true, null, Now));
    }

    [Fact]
    public void A_window_ending_exactly_now_has_not_closed_yet()
    {
        // Strictly less-than, matching `ValidTo < now` in TicketExpiryWorker. A boundary that
        // disagreed with the server would make the same ticket flip state on reconnect.
        Assert.False(TicketValidity.IsPastValidity(isActive: true, Now, Now));
    }

    // ── Kinds ────────────────────────────────────────────────────────────────────
    // A timestamp deserialised from JSON can arrive as Unspecified. Comparing that against local
    // time is wrong by the device's offset — this exact confusion already caused a bug on the write
    // path, hence ImportedTicketManager.ToUtc.

    [Fact]
    public void An_unspecified_kind_is_read_as_utc_not_local()
    {
        var justPast = DateTime.SpecifyKind(Now.AddMinutes(-1), DateTimeKind.Unspecified);
        var justFuture = DateTime.SpecifyKind(Now.AddMinutes(1), DateTimeKind.Unspecified);

        Assert.True(TicketValidity.IsPastValidity(isActive: true, justPast, Now));
        Assert.False(TicketValidity.IsPastValidity(isActive: true, justFuture, Now));
    }

    [Fact]
    public void A_local_kind_is_converted_rather_than_compared_raw()
    {
        // Whatever the runner's zone, an instant an hour in the future is not past.
        var futureLocal = Now.AddHours(1).ToLocalTime();
        Assert.Equal(DateTimeKind.Local, futureLocal.Kind);

        Assert.False(TicketValidity.IsPastValidity(isActive: true, futureLocal, Now));
    }
}
