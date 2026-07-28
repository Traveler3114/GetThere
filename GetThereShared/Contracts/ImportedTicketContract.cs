using System.ComponentModel.DataAnnotations;

using GetThereShared.Enums;

namespace GetThereShared.Contracts;

public class CreateImportedTicketRequest
{
    [MaxLength(128)]
    public string? OperatorGlobalId { get; set; }
    [MaxLength(200)]
    public string? OperatorNameSnapshot { get; set; }

    [Required]
    public ImportSource? Source { get; set; }

    [MaxLength(200)]
    public string? TicketName { get; set; }
    [MaxLength(500)]
    public string? RouteDescription { get; set; }
    [MaxLength(200)]
    public string? OriginName { get; set; }
    [MaxLength(200)]
    public string? DestinationName { get; set; }
    [Range(0, double.MaxValue)]
    public decimal? Price { get; set; }
    [MaxLength(3)]
    public string? Currency { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    [MaxLength(8000)]
    public string? RawPayload { get; set; }
    public TicketFormat? PayloadFormat { get; set; }

    /// <summary>
    /// A blob key returned by <c>POST /importedtickets/upload</c>, attaching that file to this
    /// ticket. Not a path and not free-form: the server resolves it against the caller's own
    /// unconsumed uploads and rejects anything else, so a client cannot name a file it did not
    /// upload. Required whenever <see cref="Source"/> is anything other than
    /// <see cref="ImportSource.Manual"/>.
    /// </summary>
    [MaxLength(128)]
    public string? SourceFileBlobKey { get; set; }

    /// <summary>
    /// Import anyway when this looks like a duplicate. Set only after the user has been shown the
    /// clash — two passengers on the same route on the same day are a legitimate pair of tickets,
    /// and a hard 409 gave them no way through.
    /// </summary>
    public bool AllowDuplicate { get; set; }
}

public class ImportedTicketResponse
{
    public int Id { get; set; }
    public string? OperatorGlobalId { get; set; }
    public string? OperatorNameSnapshot { get; set; }
    public ImportSource Source { get; set; }
    public ImportedTicketStatus Status { get; set; }
    public VerificationStatus Verification { get; set; }
    public string? TicketName { get; set; }
    public string? RouteDescription { get; set; }
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
    public int? JourneyId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdateImportedTicketStatusRequest
{
    [Required]
    public ImportedTicketStatus Status { get; set; }
}
