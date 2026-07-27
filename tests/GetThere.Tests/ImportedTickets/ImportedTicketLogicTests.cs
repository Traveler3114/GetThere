using GetThereAPI.Managers;

using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThere.Tests.ImportedTickets;

/// <summary>
/// The pure decision logic behind ticket import: UTC normalisation, the dedupe hash, and the status
/// state machine. None of it needs a database, so unlike the manager-level tests these run
/// anywhere — which matters, because this is the logic that silently produces wrong data rather
/// than throwing.
/// </summary>
public class ImportedTicketLogicTests
{
    private static CreateImportedTicketRequest Request(
        string? name = "Zagreb–Rijeka",
        string? route = "Zagreb Gl. Kol. → Rijeka",
        string? operatorId = null,
        decimal? price = null,
        string? currency = null,
        string? rawPayload = null) => new()
        {
            Source = ImportSource.Manual,
            TicketName = name,
            RouteDescription = route,
            OperatorGlobalId = operatorId,
            Price = price,
            Currency = currency,
            RawPayload = rawPayload
        };

    // ── UTC normalisation ───────────────────────────────────────────────────────────

    [Fact]
    public void ToUtc_passes_null_through()
    {
        Assert.Null(ImportedTicketManager.ToUtc(null));
    }

    [Fact]
    public void ToUtc_leaves_utc_untouched()
    {
        var utc = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Utc);
        var result = ImportedTicketManager.ToUtc(utc);

        Assert.Equal(utc, result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
    }

    [Fact]
    public void ToUtc_converts_local_to_utc()
    {
        var local = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Local);
        var result = ImportedTicketManager.ToUtc(local);

        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
        Assert.Equal(local.ToUniversalTime(), result.Value);
    }

    /// <summary>
    /// System.Text.Json yields Unspecified for an offset-less timestamp. Storing that verbatim is
    /// what made expiry drift by the caller's offset, since TicketExpiryWorker compares against
    /// DateTime.UtcNow.
    /// </summary>
    [Fact]
    public void ToUtc_treats_unspecified_as_utc_without_shifting_the_clock()
    {
        var naive = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Unspecified);
        var result = ImportedTicketManager.ToUtc(naive);

        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
        Assert.Equal(10, result.Value.Hour);
    }

    // ── Dedupe hash ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Hash_is_stable_for_identical_input()
    {
        var from = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1);

        Assert.Equal(
            ImportedTicketManager.ComputeDedupeHash(Request(), from, to),
            ImportedTicketManager.ComputeDedupeHash(Request(), from, to));
    }

    [Fact]
    public void Hash_prefers_raw_payload_when_present()
    {
        var withPayload = ImportedTicketManager.ComputeDedupeHash(Request(rawPayload: "UIC:918-3:ABC"), null, null);
        // Same payload, every other field different — still the same ticket as far as dedupe goes.
        var otherFields = ImportedTicketManager.ComputeDedupeHash(
            Request(name: "something else", route: "elsewhere", rawPayload: "UIC:918-3:ABC"), null, null);

        Assert.Equal(withPayload, otherFields);
    }

    /// <summary>
    /// The composite used to be joined with a bare '|', so a value containing the delimiter could
    /// shift the field boundary and collide with a differently-split ticket. These two requests
    /// produced byte-identical hash input under that scheme.
    /// </summary>
    [Fact]
    public void Hash_is_not_delimiter_injectable()
    {
        var shiftedLeft = Request(operatorId: "x", route: "y|z", name: null);
        var shiftedRight = Request(operatorId: "x|y", route: "z", name: null);

        Assert.NotEqual(
            ImportedTicketManager.ComputeDedupeHash(shiftedLeft, null, null),
            ImportedTicketManager.ComputeDedupeHash(shiftedRight, null, null));
    }

    [Fact]
    public void Hash_distinguishes_null_from_empty()
    {
        Assert.NotEqual(
            ImportedTicketManager.ComputeDedupeHash(Request(name: null), null, null),
            ImportedTicketManager.ComputeDedupeHash(Request(name: ""), null, null));
    }

    /// <summary>Two identical journeys bought at different fares are different tickets.</summary>
    [Fact]
    public void Hash_accounts_for_price_and_currency()
    {
        var cheap = ImportedTicketManager.ComputeDedupeHash(Request(price: 12.50m, currency: "EUR"), null, null);
        var dear = ImportedTicketManager.ComputeDedupeHash(Request(price: 25.00m, currency: "EUR"), null, null);
        var otherCurrency = ImportedTicketManager.ComputeDedupeHash(Request(price: 12.50m, currency: "USD"), null, null);

        Assert.NotEqual(cheap, dear);
        Assert.NotEqual(cheap, otherCurrency);
    }

    /// <summary>
    /// The hash formats dates with "O", so it must be fed already-normalised values — otherwise the
    /// same ticket hashes differently depending on how the caller expressed the offset.
    /// </summary>
    [Fact]
    public void Hash_matches_across_equivalent_date_kinds_once_normalised()
    {
        var utc = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Utc);
        var naive = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(
            ImportedTicketManager.ComputeDedupeHash(Request(), ImportedTicketManager.ToUtc(utc), null),
            ImportedTicketManager.ComputeDedupeHash(Request(), ImportedTicketManager.ToUtc(naive), null));
    }

    [Fact]
    public void Hash_is_hex_sha256()
    {
        var hash = ImportedTicketManager.ComputeDedupeHash(Request(), null, null);

        Assert.NotNull(hash);
        Assert.Equal(64, hash!.Length);
        Assert.All(hash, c => Assert.Contains(c, "0123456789abcdef"));
    }

    // ── Status transitions ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ImportedTicketStatus.Active, ImportedTicketStatus.Used)]
    [InlineData(ImportedTicketStatus.Active, ImportedTicketStatus.Expired)]
    [InlineData(ImportedTicketStatus.Active, ImportedTicketStatus.Cancelled)]
    public void Active_can_move_to_any_terminal_state(ImportedTicketStatus from, ImportedTicketStatus to)
    {
        Assert.True(ImportedTicketManager.IsValidTransition(from, to));
    }

    /// <summary>
    /// Terminal states are terminal. CancelAsync used to bypass this entirely and assign Cancelled
    /// unconditionally, so DELETE could cancel a ticket that was already Used or Expired.
    /// </summary>
    [Theory]
    [InlineData(ImportedTicketStatus.Used, ImportedTicketStatus.Cancelled)]
    [InlineData(ImportedTicketStatus.Expired, ImportedTicketStatus.Cancelled)]
    [InlineData(ImportedTicketStatus.Cancelled, ImportedTicketStatus.Active)]
    [InlineData(ImportedTicketStatus.Cancelled, ImportedTicketStatus.Used)]
    [InlineData(ImportedTicketStatus.Used, ImportedTicketStatus.Active)]
    [InlineData(ImportedTicketStatus.Expired, ImportedTicketStatus.Used)]
    [InlineData(ImportedTicketStatus.Active, ImportedTicketStatus.Active)]
    public void Terminal_and_self_transitions_are_rejected(ImportedTicketStatus from, ImportedTicketStatus to)
    {
        Assert.False(ImportedTicketManager.IsValidTransition(from, to));
    }
}
