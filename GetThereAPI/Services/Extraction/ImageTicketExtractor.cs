using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThereAPI.Services.Extraction;

/// <summary>
/// Reads a photograph or screenshot of a ticket.
/// <para>
/// This path finds <em>codes</em>, not prose: a QR or Aztec symbol decodes to the ticket payload,
/// but a photograph of a plain paper ticket carrying no code yields nothing, and says so rather
/// than inventing fields. That is the honest boundary until an OCR engine is added.
/// </para>
/// </summary>
public class ImageTicketExtractor : ITicketExtractor
{
    private readonly BarcodeDecoder _barcodes;

    public ImageTicketExtractor(BarcodeDecoder barcodes) { _barcodes = barcodes; }

    public IReadOnlyCollection<TicketFileType> SupportedTypes { get; } =
        [TicketFileType.Jpeg, TicketFileType.Png, TicketFileType.Webp, TicketFileType.Heic];

    /// <summary>
    /// A decoded symbol means the user scanned a code; a plain image is a photo of a ticket. The
    /// distinction is what the wallet later shows as provenance.
    /// </summary>
    public ImportSource SourceFor(TicketFileType fileType) => ImportSource.Photo;

    public Task<TicketExtractionResult> ExtractAsync(byte[] content, TicketFileType fileType, CancellationToken ct = default)
    {
        var result = new TicketExtractionResult();

        var decoded = _barcodes.Decode(content);
        if (decoded is not null)
        {
            result.RawPayload = decoded.Payload;
            result.PayloadFormat = decoded.Format;
            result.DetectedFields.Add(nameof(result.RawPayload));

            // Some operators encode readable text (route, dates, reference) into the payload
            // itself. UIC 918-3 rail payloads are signed binary and will yield nothing here, which
            // is fine — the payload is still stored and remains the useful part.
            TicketTextScraper.Scrape(decoded.Payload, result);
        }
        else
        {
            result.Warning = fileType == TicketFileType.Heic
                ? "We could not read a code from that HEIC image. Try taking the photo from inside the app, or fill the details in below."
                : "We could not find a barcode or QR code in that image — fill the details in below.";
        }

        return Task.FromResult(result);
    }
}
