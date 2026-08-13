using GetThereShared.Enums;

using SkiaSharp;

namespace GetThere.Services;

/// <summary>A file the user picked, normalised and ready to upload.</summary>
/// <param name="Content">The bytes to send. Already re-encoded when the source was an image.</param>
/// <param name="FileName">A display name only — the server sniffs the bytes and ignores this.</param>
/// <param name="ContentType">Likewise advisory; the server never trusts it.</param>
public record CapturedTicketFile(byte[] Content, string FileName, string ContentType);

/// <summary>
/// Picks a ticket from the camera, the photo library or the file system, and normalises images
/// before they are uploaded.
/// <para>
/// Uses the built-in <see cref="MediaPicker"/> and <see cref="FilePicker"/> rather than adding a
/// scanner package: the server already decodes QR, Aztec and PDF417 out of an uploaded image, so
/// photographing a code and scanning one are the same operation from the client's side.
/// </para>
/// </summary>
public class TicketCaptureService
{
    /// <summary>
    /// Mirrors <c>TicketUploadManager.MaxFileBytes</c>. Checked here only so an oversized pick
    /// fails immediately instead of after uploading 10 MB to be rejected; the server enforces it.
    /// <para>
    /// <b>It bounds what is sent, not what is read.</b> <see cref="ReadAsync"/> copies the whole
    /// picked file into a <c>MemoryStream</c> before this is consulted, so the check cannot save the
    /// device from a file too large to hold — by the time it runs, the bytes are already resident.
    /// A phone is where that matters: a multi-gigabyte pick is an out-of-memory kill, not an error
    /// message.
    /// </para>
    /// <para>
    /// Reaching it needs the platform's own filter to be bypassed, which is possible rather than
    /// routine — Android's <c>image/*</c> intent filter is advisory, and a file manager may hand
    /// back anything. The fix is a length check against <c>source.CanSeek ? source.Length</c>
    /// <em>before</em> the copy.
    /// </para>
    /// <para>
    /// The reason that is written down rather than applied: the pre-read ceiling cannot be this
    /// constant. An image is re-encoded down to fit it, so a 25 MB HEIC from a recent phone is a
    /// perfectly ordinary ticket photo that must still import. The pre-read limit therefore has to
    /// be a separate, much larger number, and picking it wrong rejects a real ticket at the barrier
    /// — which needs measuring against actual device captures, on a device.
    /// </para>
    /// </summary>
    public const long MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Longest edge kept when re-encoding. Generous on purpose: downscaling too far destroys the
    /// fine structure of a QR or PDF417 symbol, which is the whole point of the upload.
    /// </summary>
    private const int MaxImageEdge = 2400;

    /// <summary>Quality steps tried in order until the result fits under the size ceiling.</summary>
    private static readonly int[] JpegQualitySteps = [85, 70, 55];

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif"];

    /// <summary>Photographs a ticket, or a code on one.</summary>
    public async Task<CapturedTicketFile?> CapturePhotoAsync(CancellationToken ct = default)
    {
        if (!MediaPicker.Default.IsCaptureSupported)
            throw new NotSupportedException("This device has no camera available.");

        var photo = await MediaPicker.Default.CapturePhotoAsync();
        return photo is null ? null : await ReadAsync(photo, alwaysNormalise: true, ct);
    }

    /// <summary>Picks an existing photo or screenshot of a ticket.</summary>
    public async Task<CapturedTicketFile?> PickPhotoAsync(CancellationToken ct = default)
    {
        // PickPhotosAsync rather than the singular form, which is obsolete. Only the first is taken:
        // one ticket per import keeps the confirmation form a single, reviewable draft.
        var photos = await MediaPicker.Default.PickPhotosAsync();
        var photo = photos?.FirstOrDefault();
        return photo is null ? null : await ReadAsync(photo, alwaysNormalise: true, ct);
    }

    /// <summary>Picks an e-ticket file: a PDF, a wallet pass, a calendar invite or an image.</summary>
    public async Task<CapturedTicketFile?> PickFileAsync(CancellationToken ct = default)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Choose a ticket file",
            FileTypes = TicketFileTypes
        });

        return file is null ? null : await ReadAsync(file, alwaysNormalise: false, ct);
    }

    private static readonly FilePickerFileType TicketFileTypes = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] =
                ["application/pdf", "application/vnd.apple.pkpass", "text/calendar", "image/*"],
            [DevicePlatform.iOS] =
                ["com.adobe.pdf", "com.apple.pkpass", "com.apple.ical.ics", "public.image"],
            [DevicePlatform.MacCatalyst] =
                ["com.adobe.pdf", "com.apple.pkpass", "com.apple.ical.ics", "public.image"],
            [DevicePlatform.WinUI] =
                [".pdf", ".pkpass", ".ics", ".jpg", ".jpeg", ".png", ".webp", ".heic"]
        });

    private static async Task<CapturedTicketFile> ReadAsync(FileResult file, bool alwaysNormalise, CancellationToken ct)
    {
        await using var source = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();

        if (content.Length == 0)
            throw new InvalidOperationException("That file is empty.");

        var name = string.IsNullOrWhiteSpace(file.FileName) ? "ticket" : file.FileName;

        // The extension is a hint about whether re-encoding is worth attempting, never a security
        // decision — the server sniffs the bytes regardless. Skipping it for a PDF just avoids
        // handing 10 MB to an image decoder that will refuse it.
        var looksLikeImage = alwaysNormalise
            || ImageExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

        if (looksLikeImage && TryNormaliseImage(content, out var jpeg))
            return new CapturedTicketFile(jpeg, Path.ChangeExtension(name, ".jpg"), "image/jpeg");

        if (content.LongLength > MaxFileBytes)
            throw new InvalidOperationException(
                $"That file is {content.LongLength / 1024 / 1024} MB, over the {MaxFileBytes / 1024 / 1024} MB limit.");

        return new CapturedTicketFile(content, name, file.ContentType ?? "application/octet-stream");
    }

    /// <summary>
    /// Re-encodes an image to JPEG, applying EXIF orientation and capping the longest edge.
    /// <para>
    /// This is what makes HEIC work. iOS hands back HEIC by default, and server-side HEIC decoding
    /// is the most fragile path SkiaSharp has — <c>BarcodeDecoder</c> treats an undecodable image
    /// as "no barcode found" rather than an error, so an un-normalised HEIC silently prefills
    /// nothing. Converting on the device, where the platform's own HEIC decoder is available,
    /// turns that into an ordinary JPEG the server can always read.
    /// </para>
    /// <para>
    /// Returns false when the bytes are not a decodable image, in which case the original is
    /// uploaded untouched and the server gets its own attempt.
    /// </para>
    /// </summary>
    private static bool TryNormaliseImage(byte[] content, out byte[] jpeg)
    {
        jpeg = [];

        try
        {
            using var input = new MemoryStream(content);
            using var codec = SKCodec.Create(input);
            if (codec is null) return false;

            using var decoded = SKBitmap.Decode(codec);
            if (decoded is null) return false;

            // Each stage returns its input unchanged when it has nothing to do, so a photo needing
            // neither rotation nor downscaling costs one bitmap rather than three. A 12 MP capture
            // is ~48 MB decoded, so the copies this avoids are the difference between comfortable
            // and an out-of-memory kill on a low-end device.
            //
            // SKBitmap.Decode ignores EXIF, so a portrait phone photo decodes sideways. ZXing's
            // AutoRotate would still find the code, but the stored file is served back to the user
            // later and would be displayed rotated, so bake the orientation in here.
            var upright = ApplyOrigin(decoded, codec.EncodedOrigin);
            try
            {
                var scaled = Downscale(upright);
                try
                {
                    using var image = SKImage.FromBitmap(scaled);

                    foreach (var quality in JpegQualitySteps)
                    {
                        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
                        if (data is null) return false;

                        if (data.Size <= MaxFileBytes)
                        {
                            jpeg = data.ToArray();
                            return true;
                        }
                    }

                    return false;
                }
                finally { if (!ReferenceEquals(scaled, upright)) scaled.Dispose(); }
            }
            finally { if (!ReferenceEquals(upright, decoded)) upright.Dispose(); }
        }
        catch (Exception)
        {
            // A codec that is not present in this platform's SkiaSharp build throws rather than
            // returning null. Falling back to the original bytes is strictly better than failing
            // the import: the server may still manage it, and the user can always type the ticket.
            return false;
        }
    }

    /// <summary>Returns <paramref name="source"/> itself when it is already small enough.</summary>
    private static SKBitmap Downscale(SKBitmap source)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= MaxImageEdge) return source;

        var ratio = (float)MaxImageEdge / longest;
        var width = Math.Max(1, (int)(source.Width * ratio));
        var height = Math.Max(1, (int)(source.Height * ratio));

        // Cubic over a plain linear filter: downscaling a photographed QR or PDF417 softens the
        // module edges the decoder keys off, and Mitchell preserves them with little ringing.
        var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        return source.Resize(new SKImageInfo(width, height), sampling) ?? source;
    }

    /// <summary>
    /// Rewrites pixels so the image is upright without relying on EXIF, which JPEG re-encoding
    /// here does not carry over. Returns <paramref name="source"/> itself when already upright.
    /// </summary>
    private static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
            return source;

        var w = source.Width;
        var h = source.Height;

        // Orientations 5-8 transpose the axes, so the output is the input rotated a quarter turn.
        var swapAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        var target = new SKBitmap(swapAxes ? h : w, swapAxes ? w : h);

        // x' = ScaleX*x + SkewX*y + TransX, y' = SkewY*x + ScaleY*y + TransY.
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => Affine(-1, 0, w, 0, 1, 0),      // mirrored horizontally
            SKEncodedOrigin.BottomRight => Affine(-1, 0, w, 0, -1, h),  // rotated 180
            SKEncodedOrigin.BottomLeft => Affine(1, 0, 0, 0, -1, h),    // mirrored vertically
            SKEncodedOrigin.LeftTop => Affine(0, 1, 0, 1, 0, 0),        // transposed
            SKEncodedOrigin.RightTop => Affine(0, -1, h, 1, 0, 0),      // rotated 90 clockwise
            SKEncodedOrigin.RightBottom => Affine(0, -1, h, -1, 0, w),  // transverse
            SKEncodedOrigin.LeftBottom => Affine(0, 1, 0, -1, 0, w),    // rotated 90 anticlockwise
            _ => SKMatrix.Identity
        };

        using (var canvas = new SKCanvas(target))
        using (var image = SKImage.FromBitmap(source))
        {
            canvas.SetMatrix(matrix);

            // Default sampling is enough: the matrix only ever rotates or mirrors by whole
            // quarter-turns here, so no pixel is resampled between grid positions.
            canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default, null);
        }

        return target;
    }

    private static SKMatrix Affine(float scaleX, float skewX, float transX, float skewY, float scaleY, float transY) =>
        new()
        {
            ScaleX = scaleX,
            SkewX = skewX,
            TransX = transX,
            SkewY = skewY,
            ScaleY = scaleY,
            TransY = transY,
            Persp2 = 1
        };
}
