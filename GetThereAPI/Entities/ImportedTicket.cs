using GetThereShared.Enums;

namespace GetThereAPI.Entities;

public class ImportedTicket
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    /// <summary>
    /// The device's own id for this ticket, when it was created on one.
    /// <para>
    /// This is the idempotency key for the offline import queue, and it exists because the dedupe
    /// hash cannot serve as one. That hash is computed from the request's fields, so a ticket edited
    /// between being queued and being pushed produces a different hash and inserts twice; and its
    /// unique index is filtered on <c>Status = 'Active'</c>, so a ticket marked used before the
    /// queue drained inserts again too. A client-minted GUID has neither problem.
    /// </para>
    /// <para>
    /// Null for anything created directly against the API, which is why the unique index below is
    /// filtered — SQL Server treats NULLs as equal in a unique index, so an unfiltered one would
    /// allow exactly one server-created ticket per user.
    /// </para>
    /// </summary>
    public Guid? ClientId { get; set; }

    public string? OperatorGlobalId { get; set; }
    public string? OperatorNameSnapshot { get; set; }

    public ImportSource Source { get; set; }
    public ImportedTicketStatus Status { get; set; } = ImportedTicketStatus.Active;
    public VerificationStatus Verification { get; set; } = VerificationStatus.Unverified;

    public string? TicketName { get; set; }
    public string? RouteDescription { get; set; }

    /// <summary>
    /// Structured endpoints, populated by extraction where the source carries them and editable by
    /// hand otherwise. Journey grouping chains one leg's destination to the next leg's origin,
    /// which free-text <see cref="RouteDescription"/> cannot support.
    /// </summary>
    public string? OriginName { get; set; }
    public string? DestinationName { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public string? RawPayload { get; set; }
    public TicketFormat? PayloadFormat { get; set; }

    public string? SourceFileBlobKey { get; set; }
    public string? SourceFileContentType { get; set; }

    public string? DedupeHash { get; set; }

    /// <summary>The trip this ticket belongs to, if the user has grouped it into one.</summary>
    public int? JourneyId { get; set; }
    public Journey? Journey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
