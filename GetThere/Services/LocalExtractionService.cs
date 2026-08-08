using System.Diagnostics;

using GetThereShared.Contracts;
using GetThereShared.Enums;
using GetThereShared.Extraction;

namespace GetThere.Services;

/// <summary>
/// Runs ticket extraction on the device.
/// <para>
/// Every import path used to require the server: photo, file and paste each called an authorized
/// endpoint before the confirmation form opened. That made importing a ticket impossible without an
/// account and impossible without signal — for a wallet whose premise is holding tickets the user
/// already has, both are the wrong way round.
/// </para>
/// <para>
/// The extractors are the same code the API runs, from GetThereShared, so a ticket read here and
/// a ticket read there cannot disagree. What is <em>not</em> here is PDF, wallet passes and calendar
/// invites: those extractors stayed server-side (PdfPig and Ical.Net, and AOT/trimming on iOS is a
/// real risk), so those formats still need an account. <see cref="CanExtractLocally"/> is how a
/// caller finds out before promising the user anything.
/// </para>
/// </summary>
public class LocalExtractionService
{
    private readonly BarcodeDecoder _barcodeDecoder;
    private readonly ImageTicketExtractor _imageExtractor;

    public LocalExtractionService()
    {
        _barcodeDecoder = new BarcodeDecoder((message, error) =>
            Trace.WriteLine($"[LocalExtraction] {message}{(error is null ? "" : $": {error.Message}")}"));

        _imageExtractor = new ImageTicketExtractor(_barcodeDecoder);
    }

    /// <summary>
    /// Whether this device can read the given file type without the server.
    /// </summary>
    /// <remarks>
    /// HEIC is excluded even though the extractor claims it: SkiaSharp cannot decode it on every
    /// platform build, and <c>TicketCaptureService</c> already re-encodes camera output to JPEG for
    /// that reason. A picked-from-disk HEIC goes to the server, which has the same limitation but at
    /// least logs it in one place.
    /// </remarks>
    public static bool CanExtractLocally(TicketFileType? fileType) =>
        fileType is TicketFileType.Jpeg or TicketFileType.Png or TicketFileType.Webp;

    /// <summary>
    /// Identifies a file from its leading bytes, exactly as the upload endpoint does. Null means the
    /// bytes match no signature the app accepts.
    /// </summary>
    public static TicketFileType? Sniff(byte[] content) => TicketFileSniffer.Detect(content);

    /// <summary>
    /// Reads what it can from an image, or returns an empty result.
    /// <para>
    /// An empty result is an ordinary outcome, not a failure: a photograph of a paper ticket with no
    /// barcode on it yields nothing, and the confirmation form opens blank for the user to fill in.
    /// Inventing fields would put wrong data in a wallet silently.
    /// </para>
    /// </summary>
    public async Task<TicketExtractionResult> ExtractImageAsync(byte[] content, TicketFileType fileType)
    {
        try
        {
            return await _imageExtractor.ExtractAsync(content, fileType);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[LocalExtraction] Image extraction failed: {ex.Message}");
            return new TicketExtractionResult();
        }
    }

    /// <summary>
    /// Scrapes pasted confirmation text. Pure regex over the string — the same scraper the server's
    /// <c>extract-text</c> endpoint calls, so pasting works offline and signed out.
    /// </summary>
    public static TicketExtractionResult ExtractText(string text)
    {
        var result = new TicketExtractionResult();
        try
        {
            // Scrape fills a result rather than returning one, so partial reads accumulate across
            // sources — the server composes PDF text and barcode payload into a single draft the
            // same way.
            TicketTextScraper.Scrape(text, result);
            return result;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[LocalExtraction] Text extraction failed: {ex.Message}");
            return result;
        }
    }
}
