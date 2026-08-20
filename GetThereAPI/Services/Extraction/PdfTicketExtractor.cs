using System.Text;

using Docnet.Core;
using Docnet.Core.Models;

using GetThereAPI.Exceptions;

using GetThereShared.Contracts;
using GetThereShared.Enums;
using GetThereShared.Extraction;

using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace GetThereAPI.Services.Extraction;

/// <summary>
/// Reads a PDF e-ticket two ways: the text layer for route, dates and price, and a rendered image of
/// the page for the barcode operators print on the ticket.
/// <para>
/// The barcode is the more valuable of the two — it is the machine-readable ticket itself, and it
/// survives layout differences between operators that defeat text scraping. It is read by rasterising
/// the page and scanning the rendered image, rather than by pulling the embedded image back out of
/// the PDF: many tickets store the code in an encoding PdfPig cannot turn back into a decodable image
/// (a FlateDecode QR, for one), which is why that approach reported "no QR" on real boarding passes.
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

    /// <summary>
    /// Pages rasterised for the barcode. The code sits on the ticket itself, at the front; rendering
    /// a whole document at scanning resolution is unbounded work an upload should not buy.
    /// </summary>
    private const int MaxRasterPages = 5;

    /// <summary>
    /// Render resolution for the barcode pass. 200 DPI reads dense PDF417/Aztec and QR reliably while
    /// keeping an A4 page under ~4 MP — well below <c>BarcodeDecoder</c>'s pixel ceiling.
    /// </summary>
    private const int RenderDpi = 200;

    /// <summary>
    /// PDFium (through Docnet) is not thread-safe and <see cref="DocLib.Instance"/> is a process-wide
    /// singleton, so renders are serialised. Uploads are rate-limited (10/min), so this is not a
    /// throughput concern.
    /// </summary>
    private static readonly object RenderLock = new();

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

            foreach (var page in document.GetPages().Take(MaxPages))
            {
                ct.ThrowIfCancellationRequested();
                text.AppendLine(ExtractLines(page));
            }

            TicketTextScraper.Scrape(text.ToString(), result);
        }

        ScanForBarcode(content, result, ct);

        if (result.DetectedFields.Count == 0)
        {
            result.Warning = "We could not read any ticket details from that PDF — fill them in below.";
            _logger.LogInformation("PDF parsed but yielded no recognised ticket fields");
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Renders the first pages and scans each rendered image for a barcode, stopping at the first hit.
    /// A rendering failure (an unrenderable page, or the native library being unavailable) yields no
    /// barcode rather than failing the upload — the text pass may still have found fields.
    /// </summary>
    private void ScanForBarcode(byte[] content, TicketExtractionResult result, CancellationToken ct)
    {
        try
        {
            lock (RenderLock)
            {
                using var docReader = DocLib.Instance.GetDocReader(content, new PageDimensions(RenderDpi / 72.0));

                var pageCount = Math.Min(docReader.GetPageCount(), MaxRasterPages);
                for (var i = 0; i < pageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    byte[] bgra;
                    int width, height;
                    try
                    {
                        using var pageReader = docReader.GetPageReader(i);
                        bgra = pageReader.GetImage();
                        width = pageReader.GetPageWidth();
                        height = pageReader.GetPageHeight();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation(ex, "PDF page {Page} could not be rendered for barcode scanning", i);
                        continue;
                    }

                    var decoded = _barcodes.DecodeBgra(bgra, width, height);
                    if (decoded is null) continue;

                    result.RawPayload = decoded.Payload;
                    result.PayloadFormat = decoded.Format;
                    result.DetectedFields.Add(nameof(result.RawPayload));
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "PDF barcode rasterisation pass failed");
        }
    }

    /// <summary>
    /// Reconstructs the page's visual lines from PdfPig word positions. <c>page.Text</c> arrives as
    /// one run-together string with no line breaks — and not even reliable double-spaces between
    /// columns — so the scraper's line-oriented route match, and even token boundaries (a date glued
    /// to the time after it), are lost without this. Words are grouped into rows by baseline height
    /// (PDF Y increases upward, so rows read top-down by descending Y) and ordered left to right.
    /// </summary>
    internal static string ExtractLines(Page page)
    {
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        if (words.Count == 0) return page.Text;

        // Two words belong to the same row when their baselines sit within this many PDF units.
        const double rowTolerance = 3.0;

        var rows = new List<(double Y, List<Word> Words)>();
        foreach (var word in words)
        {
            var y = word.BoundingBox.Bottom;
            var row = rows.FirstOrDefault(r => Math.Abs(r.Y - y) <= rowTolerance);
            if (row.Words is null)
            {
                row = (y, []);
                rows.Add(row);
            }
            row.Words.Add(word);
        }

        return string.Join('\n', rows
            .OrderByDescending(r => r.Y)
            .Select(r => string.Join(' ', r.Words
                .OrderBy(w => w.BoundingBox.Left)
                .Select(w => w.Text))));
    }
}
