using GetThereAPI.Managers;

using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThere.Tests.ImportedTickets;

/// <summary>
/// The idempotency key for the offline import queue.
/// <para>
/// These pin the reason <c>ClientId</c> exists rather than the plumbing that uses it. The dedupe
/// hash already in <c>ImportedTicketManager</c> looks like it could serve as an idempotency key and
/// cannot, in two specific ways — both reproduced below against the real hash function, because if
/// either ever stopped being true the extra column would be dead weight and someone would remove it.
/// </para>
/// </summary>
public class ImportedTicketClientIdTests
{
    private static CreateImportedTicketRequest Request(string? name = "Zagreb → Split") => new()
    {
        Source = ImportSource.Manual,
        TicketName = name,
        RouteDescription = "HŽPP 521",
        ValidFrom = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
        ValidTo = new DateTime(2026, 8, 1, 14, 0, 0, DateTimeKind.Utc),
        Price = 24.50m,
        Currency = "EUR"
    };

    [Fact]
    public void The_dedupe_hash_changes_when_the_user_edits_the_draft()
    {
        // Why a queued import cannot be identified by its hash: a ticket sitting in the queue is
        // still editable, and any edit makes the retry look like a different ticket, which inserts
        // a second row.
        var before = ImportedTicketManager.ComputeDedupeHash(
            Request(), Request().ValidFrom, Request().ValidTo);

        var after = ImportedTicketManager.ComputeDedupeHash(
            Request("Zagreb → Rijeka"), Request().ValidFrom, Request().ValidTo);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void The_same_draft_hashes_the_same_way()
    {
        // The other half: the hash is deterministic, so it does catch a genuinely identical resend.
        // It is only the *edited* and *non-Active* cases it cannot cover.
        var a = ImportedTicketManager.ComputeDedupeHash(
            Request(), Request().ValidFrom, Request().ValidTo);
        var b = ImportedTicketManager.ComputeDedupeHash(
            Request(), Request().ValidFrom, Request().ValidTo);

        Assert.Equal(a, b);
    }

    [Fact]
    public void A_payload_alone_decides_the_hash_when_there_is_one()
    {
        // Documents the two-tier key: with a RawPayload the other fields are ignored, so two
        // differently-named drafts of the same scanned ticket collide — correct for dedupe, and
        // another reason it cannot double as a per-import identity.
        var withPayload = Request();
        withPayload.RawPayload = "UIC:918-3:ABC123";

        var renamed = Request("Something else entirely");
        renamed.RawPayload = "UIC:918-3:ABC123";

        Assert.Equal(
            ImportedTicketManager.ComputeDedupeHash(withPayload, null, null),
            ImportedTicketManager.ComputeDedupeHash(renamed, null, null));
    }

    [Fact]
    public void A_client_id_survives_the_edits_a_hash_does_not()
    {
        // The property that makes it usable as an idempotency key: it is minted once, at creation,
        // and carried unchanged through every edit and every retry.
        var request = Request();
        request.ClientId = Guid.NewGuid();
        var original = request.ClientId;

        request.TicketName = "Edited before the queue drained";
        request.Price = 31.00m;

        Assert.Equal(original, request.ClientId);
    }
}
