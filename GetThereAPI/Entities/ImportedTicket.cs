using GetThereShared.Enums;

namespace GetThereAPI.Entities;

public class ImportedTicket
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

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

    public int? JourneyId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
