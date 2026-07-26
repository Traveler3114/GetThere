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
    [Range(0, double.MaxValue)]
    public decimal? Price { get; set; }
    [MaxLength(3)]
    public string? Currency { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    [MaxLength(8000)]
    public string? RawPayload { get; set; }
    public TicketFormat? PayloadFormat { get; set; }
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
