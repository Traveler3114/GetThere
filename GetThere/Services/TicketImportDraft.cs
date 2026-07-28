using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThere.Services;

/// <summary>
/// A proposed ticket on its way to the import form, from whichever source the user chose.
/// <para>
/// Nothing exists on the server yet: an upload has been stored and read, but no ticket is created
/// until the user confirms the form. Every source funnels through here so the form stays the single
/// confirmation and validation surface rather than each capture path growing its own.
/// </para>
/// </summary>
public class TicketImportDraft
{
    /// <summary>Shell query key. Passed as an object, so it is never round-tripped through a string.</summary>
    public const string QueryKey = "draft";

    public ImportSource Source { get; init; } = ImportSource.Manual;

    /// <summary>Single-use handle to the stored file, or null for pasted text and manual entry.</summary>
    public string? BlobKey { get; init; }

    public TicketExtractionResult Extraction { get; init; } = new();

    /// <summary>Builds a draft from an upload, deriving provenance from the sniffed file type.</summary>
    /// <remarks>
    /// The source is decided here rather than taken from the response because
    /// <c>TicketUploadResponse</c> carries no source — the server's <c>ITicketExtractor.SourceFor</c>
    /// exists but is never surfaced by the endpoint. <c>FileType</c> is the sniffed truth, so
    /// deriving from it still means the bytes decide, not the filename.
    /// </remarks>
    public static TicketImportDraft FromUpload(TicketUploadResponse upload) => new()
    {
        Source = SourceFor(upload.FileType, upload.Extraction),
        BlobKey = upload.BlobKey,
        Extraction = upload.Extraction
    };

    /// <summary>Builds a draft from scraped text. No file was stored, so there is no blob key.</summary>
    public static TicketImportDraft FromText(TicketExtractionResult extraction) => new()
    {
        Source = ImportSource.Text,
        Extraction = extraction
    };

    private static ImportSource SourceFor(TicketFileType fileType, TicketExtractionResult extraction) => fileType switch
    {
        TicketFileType.Pdf => ImportSource.Pdf,
        TicketFileType.PkPass => ImportSource.PkPass,
        TicketFileType.ICalendar => ImportSource.Calendar,

        // An image that yielded a symbol is a scan; one that did not is a photograph of a ticket.
        // The distinction is what the wallet later shows as provenance, and it also feeds the
        // dedupe hash — deterministic either way, since it derives from the file's own content.
        _ => extraction.PayloadFormat is not null ? ImportSource.QrScan : ImportSource.Photo
    };
}
