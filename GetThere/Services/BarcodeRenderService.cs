using System.Diagnostics;
using System.Runtime.InteropServices;

using GetThereShared.Common;
using GetThereShared.Enums;

using SkiaSharp;

using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;
using ZXing.Rendering;

namespace GetThere.Services;

/// <summary>
/// Turns a ticket's payload into a scannable image.
/// <para>
/// Until this existed nothing in the app rendered a code at all — the ticket screen printed the
/// payload as monospace text inside a dashed square standing in for a QR, and the page said as much
/// in its own header comment. A wallet whose ticket cannot be scanned at a barrier is not a wallet.
/// </para>
/// </summary>
public class BarcodeRenderService
{
    /// <summary>
    /// Pixel size of the rendered square. Generous on purpose: a scanner reads *modules*, so the
    /// image must carry more pixels than the ~210dip it is displayed at, and be scaled down rather
    /// than up. An upscaled small bitmap blurs the module edges and stops reading.
    /// </summary>
    private const int RenderSize = 720;

    /// <summary>Height for linear symbologies, which are wide rather than square.</summary>
    private const int LinearHeight = 240;

    /// <summary>
    /// Translates the chosen symbology into the encoder's own enum.
    /// <para>
    /// Only the translation lives here. <b>Which</b> symbology a payload may be drawn as — and when
    /// the answer is "none" — is in <see cref="TicketBarcode.ChooseSymbology"/>, in GetThereShared,
    /// because the test project cannot reference this one and that decision is the part worth
    /// covering.
    /// </para>
    /// </summary>
    private static BarcodeFormat ToEncoderFormat(BarcodeSymbology symbology) => symbology switch
    {
        BarcodeSymbology.QrCode => BarcodeFormat.QR_CODE,
        BarcodeSymbology.Code128 => BarcodeFormat.CODE_128,
        _ => throw new ArgumentOutOfRangeException(nameof(symbology), symbology, "No encoder mapping.")
    };

    /// <summary>
    /// Renders <paramref name="payload"/>, or returns null when there is nothing safe to draw.
    /// <para>
    /// Null is a normal outcome, not a failure: an empty payload, a format with no symbology, or a
    /// payload that will not round-trip all end here, and every caller is expected to have a text
    /// fallback ready.
    /// </para>
    /// </summary>
    public ImageSource? Render(string? payload, TicketFormat format)
    {
        var symbology = TicketBarcode.ChooseSymbology(format, payload);
        if (symbology is null) return null;

        var encoderFormat = ToEncoderFormat(symbology.Value);

        try
        {
            var pixelData = Write(payload!, encoderFormat);
            var png = ToPng(pixelData);

            // FromStream's factory is invoked per load, so it must hand back a fresh, rewound
            // stream each time rather than one the first consumer has already read to the end.
            return ImageSource.FromStream(() => new MemoryStream(png, writable: false));
        }
        catch (Exception ex)
        {
            // A payload the encoder rejects is a data problem, not a crash. The screen still has the
            // text to show, and swallowing this quietly is why the log line is here.
            Trace.WriteLine($"[BarcodeRenderService] Could not render a {encoderFormat} for a {payload!.Length}-char payload: {ex.Message}");
            return null;
        }
    }

    private static PixelData Write(string payload, BarcodeFormat symbology)
    {
        var isSquare = symbology is BarcodeFormat.QR_CODE;

        var writer = new BarcodeWriterPixelData
        {
            Format = symbology,
            Options = isSquare
                ? new QrCodeEncodingOptions
                {
                    Width = RenderSize,
                    Height = RenderSize,

                    // Quiet zone in modules. The QR spec asks for 4; scanners tolerate less, but a
                    // ticket is often shown on a busy screen where the margin is what separates the
                    // symbol from whatever is behind it.
                    Margin = 4,

                    // Q corrects ~25%. Higher than the usual default because this is read off a
                    // phone screen — fingerprints, glare and a cracked panel all eat modules.
                    ErrorCorrection = ErrorCorrectionLevel.Q,

                    CharacterSet = "UTF-8"
                }
                : new EncodingOptions
                {
                    Width = RenderSize,
                    Height = LinearHeight,
                    Margin = 10,
                    PureBarcode = true
                }
        };

        return writer.Write(payload);
    }

    /// <summary>
    /// Copies ZXing's BGRA buffer into a Skia bitmap and encodes it.
    /// <para>
    /// PNG rather than JPEG deliberately: a barcode is hard edges on flat colour, which is where
    /// lossy compression puts its artefacts, and those artefacts land exactly on the module
    /// boundaries a scanner measures.
    /// </para>
    /// </summary>
    private static byte[] ToPng(PixelData pixelData)
    {
        var info = new SKImageInfo(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var bitmap = new SKBitmap();
        var handle = GCHandle.Alloc(pixelData.Pixels, GCHandleType.Pinned);
        try
        {
            // InstallPixels over the pinned buffer avoids a second copy; the handle stays alive
            // until the encode below has finished reading through it.
            if (!bitmap.InstallPixels(info, handle.AddrOfPinnedObject(), info.RowBytes))
                throw new InvalidOperationException("Skia rejected the encoder's pixel buffer.");

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("Skia could not encode the barcode as PNG.");

            return encoded.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }
}
