using GetThereShared.Enums;

using SkiaSharp;

using ZXing;
using ZXing.Common;

namespace GetThereExtraction;

/// <summary>A barcode found in an uploaded file.</summary>
/// <param name="Payload">The decoded text, stored as the ticket's raw payload.</param>
/// <param name="Format">How it is encoded, for rendering it back to the user later.</param>
public record DecodedBarcode(string Payload, TicketFormat Format);

/// <summary>
/// Decodes the barcode formats transport tickets actually use.
/// <para>
/// Aztec and PDF417 matter as much as QR here: European rail tickets following UIC 918-3 use them,
/// and a decoder limited to QR would miss most train tickets. Note this reads <em>codes</em>, not
/// prose — a photograph of a paper ticket bearing no barcode yields nothing, which is why callers
/// treat a null result as "nothing to prefill" rather than as a failure.
/// </para>
/// </summary>
public class BarcodeDecoder
{
    /// <summary>
    /// Where diagnostics go, if anywhere.
    /// <para>
    /// A delegate rather than <c>ILogger</c> so this project needs no logging dependency: it runs
    /// inside a MAUI app as well as an API, and a shared library forcing a logging abstraction onto
    /// a phone for four warning lines is a poor trade. GetThereAPI passes its logger in; the client
    /// passes a Trace write, or nothing.
    /// </para>
    /// </summary>
    private readonly Action<string, Exception?>? _log;

    public BarcodeDecoder(Action<string, Exception?>? log = null) { _log = log; }

    private void Log(string message, Exception? error = null) => _log?.Invoke(message, error);

    private static readonly BarcodeFormat[] TicketFormats =
    [
        BarcodeFormat.QR_CODE,
        BarcodeFormat.AZTEC,
        BarcodeFormat.PDF_417,
        BarcodeFormat.DATA_MATRIX,
        BarcodeFormat.CODE_128,
        BarcodeFormat.CODE_39,
        BarcodeFormat.EAN_13,
        BarcodeFormat.ITF
    ];

    /// <summary>
    /// Ceiling on the decoded surface, in pixels.
    /// <para>
    /// The upload limit bounds the <em>compressed</em> bytes, which says nothing about what they
    /// expand to: a few KB of PNG can declare 20000x20000 and allocate 1.6 GB. That is why the
    /// dimensions are read from the header and checked <b>before</b> any pixel buffer is allocated.
    /// 40 megapixels is far above any ticket photograph — a 108 MP phone camera at full resolution
    /// is 12000x9000 — and far below a size that threatens the process.
    /// </para>
    /// </summary>
    private const long MaxDecodedPixels = 40_000_000;

    /// <summary>Decodes the first barcode in an encoded image, or null if there is none.</summary>
    public DecodedBarcode? Decode(byte[] imageBytes)
    {
        try
        {
            using var codecStream = new MemoryStream(imageBytes, writable: false);
            using var codec = SKCodec.Create(codecStream);
            if (codec is null)
            {
                // HEIC in particular is not decodable by every SkiaSharp native build. The client
                // re-encodes camera captures to JPEG for this reason; a picked-from-disk HEIC that
                // lands here simply yields no barcode rather than failing the upload.
                Log("Image could not be decoded for barcode scanning");
                return null;
            }

            // Header only — no pixels have been allocated at this point.
            var info = codec.Info;
            var pixels = (long)info.Width * info.Height;
            if (pixels > MaxDecodedPixels)
            {
                Log($"Refusing to decode a {info.Width}x{info.Height} image ({pixels} px) for barcode scanning; the ceiling is {MaxDecodedPixels} px");
                return null;
            }

            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap is null)
            {
                Log("Image could not be decoded for barcode scanning");
                return null;
            }

            return Decode(bitmap);
        }
        // Deliberately not a bare `catch`: an OutOfMemoryException means the process is already in
        // trouble, and swallowing it here reported "no barcode found" while leaving it unrecoverable.
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            Log("Barcode decoding failed", ex);
            return null;
        }
    }

    private static DecodedBarcode? Decode(SKBitmap bitmap)
    {
        using var rgba = bitmap.ColorType == SKColorType.Rgba8888
            ? null
            : bitmap.Copy(SKColorType.Rgba8888);
        var source = rgba ?? bitmap;

        var pixels = source.Bytes;
        if (pixels is null || pixels.Length == 0) return null;

        var luminance = new RGBLuminanceSource(pixels, source.Width, source.Height,
            RGBLuminanceSource.BitmapFormat.RGBA32);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                // Ticket photos are rarely square-on or well-lit, so the extra passes are worth
                // the cost on a one-off import.
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = TicketFormats
            }
        };

        var result = reader.Decode(luminance);
        if (result is null || string.IsNullOrWhiteSpace(result.Text)) return null;

        return new DecodedBarcode(result.Text, ToTicketFormat(result.BarcodeFormat));
    }

    internal static TicketFormat ToTicketFormat(BarcodeFormat format) => format switch
    {
        BarcodeFormat.QR_CODE => TicketFormat.QR,
        BarcodeFormat.DATA_MATRIX => TicketFormat.QR,
        _ => TicketFormat.Barcode
    };

    /// <summary>
    /// Maps a wallet pass's declared barcode format, which arrives as a string rather than as a
    /// scanned symbol.
    /// </summary>
    internal static TicketFormat FromPkPassFormat(string? pkFormat) =>
        pkFormat?.Contains("QR", StringComparison.OrdinalIgnoreCase) == true
            ? TicketFormat.QR
            : TicketFormat.Barcode;
}
