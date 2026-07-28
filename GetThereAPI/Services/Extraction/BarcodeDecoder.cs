using GetThereShared.Enums;

using SkiaSharp;

using ZXing;
using ZXing.Common;

namespace GetThereAPI.Services.Extraction;

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
    private readonly ILogger<BarcodeDecoder> _logger;

    public BarcodeDecoder(ILogger<BarcodeDecoder> logger) { _logger = logger; }

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

    /// <summary>Decodes the first barcode in an encoded image, or null if there is none.</summary>
    public DecodedBarcode? Decode(byte[] imageBytes)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(imageBytes);
            if (bitmap is null)
            {
                // HEIC in particular is not decodable by every SkiaSharp native build. The client
                // re-encodes camera captures to JPEG for this reason; a picked-from-disk HEIC that
                // lands here simply yields no barcode rather than failing the upload.
                _logger.LogInformation("Image could not be decoded for barcode scanning");
                return null;
            }

            return Decode(bitmap);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Barcode decoding failed");
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
