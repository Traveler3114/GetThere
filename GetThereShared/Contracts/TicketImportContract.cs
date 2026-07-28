using GetThereShared.Enums;

namespace GetThereShared.Contracts;

/// <summary>
/// Everything an extractor could read off an uploaded file. Every field is a candidate, not a
/// commitment — the user confirms or corrects them in the import form before a ticket exists.
/// </summary>
public class TicketExtractionResult
{
    public string? TicketName { get; set; }
    public string? RouteDescription { get; set; }

    /// <summary>
    /// Structured endpoints, where the format carries them (a wallet pass always does, a PDF
    /// sometimes). These are what journey grouping chains on, which free-text
    /// <see cref="RouteDescription"/> cannot support.
    /// </summary>
    public string? OriginName { get; set; }
    public string? DestinationName { get; set; }

    public string? OperatorNameSnapshot { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    /// <summary>The decoded barcode payload, when the file carried one.</summary>
    public string? RawPayload { get; set; }
    public TicketFormat? PayloadFormat { get; set; }

    /// <summary>
    /// Names of the fields above that were read directly from the file rather than inferred, so
    /// the UI can distinguish "this is what your ticket says" from "this is our best guess".
    /// </summary>
    public List<string> DetectedFields { get; set; } = [];

    /// <summary>Set when the file was readable but yielded nothing worth prefilling.</summary>
    public string? Warning { get; set; }
}

/// <summary>Pasted confirmation text to scrape for ticket fields.</summary>
public class ExtractTicketTextRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(20000)]
    public string Text { get; set; } = string.Empty;
}

/// <summary>Result of <c>POST /importedtickets/upload</c>.</summary>
public class TicketUploadResponse
{
    /// <summary>
    /// Server-minted handle to the stored file. Pass it back as
    /// <see cref="CreateImportedTicketRequest.SourceFileBlobKey"/> to attach the file to the ticket.
    /// Single-use and scoped to the uploading user.
    /// </summary>
    public string BlobKey { get; set; } = string.Empty;

    public TicketFileType FileType { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>The suggested ticket, for the user to confirm.</summary>
    public TicketExtractionResult Extraction { get; set; } = new();
}
