using System.Text;

using GetThereAPI.Exceptions;

using GetThereShared.Contracts;
using GetThereShared.Enums;

using UglyToad.PdfPig;

namespace GetThereAPI.Services.Extraction;

/// <summary>
/// Reads a PDF e-ticket two ways: the text layer for route, dates and price, and any embedded
/// image for the barcode operators print on the ticket.
/// <para>
/// The barcode is the more valuable of the two — it is the machine-readable ticket itself, and it
/// survives layout differences between operators that defeat text scraping.
/// </para>
/// </summary>
public class PdfTicketExtractor : ITicketExtractor
{
    private readonly BarcodeDecoder _barcodes;
    private readonly ILogger<PdfTicketExtractor> _logger;

    public PdfTicketExtractor(BarcodeDecoder barcodes, ILogger<PdfTicketExtractor> logger)
    {
        _barcodes = barcodes;
        _logger = logger;
    }

    public IReadOnlyCollection<TicketFileType> SupportedTypes { get; } = [TicketFileType.Pdf];

    public ImportSource SourceFor(TicketFileType fileType) => ImportSource.Pdf;

    /// <summary>An e-ticket is a page or two; beyond this it is a document, not a ticket.</summary>
    private const int MaxPages = 20;

    /// <summary>Scanning every image on every page is unbounded work an upload should not buy.</summary>
    private const int MaxImagesScanned = 12;

    public Task<TicketExtractionResult> ExtractAsync(byte[] content, TicketFileType fileType, CancellationToken ct = default)
    {
        var result = new TicketExtractionResult();

        PdfDocument document;
        try
        {
            document = PdfDocument.Open(content);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "PDF could not be opened for ticket extraction");
            throw new AppException("That PDF could not be read.", 400);
        }

        using (document)
        {
            var text = new StringBuilder();
            var imagesScanned = 0;

            foreach (var page in document.GetPages().Take(MaxPages))
            {
                ct.ThrowIfCancellationRequested();

                text.AppendLine(page.Text);

                if (result.RawPayload is not null || imagesScanned >= MaxImagesScanned) continue;

                foreach (var image in page.GetImages())
                {
                    if (imagesScanned++ >= MaxImagesScanned) break;

                    // TryGetPng re-encodes to something SkiaSharp can decode; RawBytes is the
                    // stream as stored, which for a DCTDecode image is already a JPEG.
                    var bytes = image.TryGetPng(out var png) ? png : image.RawBytes.ToArray();
                    if (bytes.Length == 0) continue;

                    var decoded = _barcodes.Decode(bytes);
                    if (decoded is null) continue;

                    result.RawPayload = decoded.Payload;
                    result.PayloadFormat = decoded.Format;
                    result.DetectedFields.Add(nameof(result.RawPayload));
                    break;
                }
            }

            // PdfPig lays text out per page with no line breaks between visual rows, so the
            // scraper's line-oriented route match needs the page text split up first.
            TicketTextScraper.Scrape(NormaliseLayout(text.ToString()), result);
        }

        if (result.DetectedFields.Count == 0)
        {
            result.Warning = "We could not read any ticket details from that PDF — fill them in below.";
            _logger.LogInformation("PDF parsed but yielded no recognised ticket fields");
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Breaks a run-together text layer into plausible lines. Multiple spaces are how PdfPig
    /// renders column gaps, and those gaps are usually where a visual line ended.
    /// </summary>
    internal static string NormaliseLayout(string text) =>
        string.Join('\n', text
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split(['\n'], StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(line => line.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
}
