using System.IO.Compression;
using System.Text;
using System.Text.Json;

using GetThereAPI.Exceptions;
using GetThereAPI.Services.Extraction;

using GetThereShared.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using SkiaSharp;

using ZXing;

namespace GetThere.Tests.ImportedTickets;

/// <summary>
/// End-to-end extraction per format. Fixtures are built in-process rather than committed as
/// binaries, so what each test asserts is visible in the test itself — and the barcode cases
/// round-trip through a real encode, which proves the decoder against genuine symbol data rather
/// than a recorded blob.
/// </summary>
public class TicketExtractionTests
{
    private static BarcodeDecoder Decoder() => new(NullLogger<BarcodeDecoder>.Instance);

    // ── Barcode decoding ────────────────────────────────────────────────────────────

    /// <summary>Renders a real symbol so the decoder is tested against actual barcode data.</summary>
    private static byte[] EncodeBarcodePng(string payload, BarcodeFormat format, int size = 320)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = format,
            Options = new ZXing.Common.EncodingOptions { Width = size, Height = size, Margin = 8 }
        };

        var pixelData = writer.Write(payload);

        using var bitmap = new SKBitmap(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(pixelData.Pixels, 0, bitmap.GetPixels(), pixelData.Pixels.Length);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    [Fact]
    public void Decodes_a_qr_code_from_an_image()
    {
        var png = EncodeBarcodePng("TICKET-REF-8821", BarcodeFormat.QR_CODE);

        var decoded = Decoder().Decode(png);

        Assert.NotNull(decoded);
        Assert.Equal("TICKET-REF-8821", decoded!.Payload);
        Assert.Equal(TicketFormat.QR, decoded.Format);
    }

    /// <summary>
    /// Aztec is what UIC 918-3 rail tickets actually carry, so a decoder that only handled QR would
    /// miss most European train tickets.
    /// </summary>
    [Fact]
    public void Decodes_an_aztec_code_as_a_barcode()
    {
        var png = EncodeBarcodePng("UIC-918-3-PAYLOAD", BarcodeFormat.AZTEC);

        var decoded = Decoder().Decode(png);

        Assert.NotNull(decoded);
        Assert.Equal("UIC-918-3-PAYLOAD", decoded!.Payload);
        Assert.Equal(TicketFormat.Barcode, decoded.Format);
    }

    [Fact]
    public void Decodes_pdf417()
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.PDF_417,
            Options = new ZXing.Common.EncodingOptions { Width = 600, Height = 200, Margin = 8 }
        };
        var pixelData = writer.Write("BOARDING-PASS-XYZ");

        using var bitmap = new SKBitmap(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(pixelData.Pixels, 0, bitmap.GetPixels(), pixelData.Pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        var decoded = Decoder().Decode(encoded.ToArray());

        Assert.NotNull(decoded);
        Assert.Equal("BOARDING-PASS-XYZ", decoded!.Payload);
    }

    /// <summary>A photo with no code is the common case, and must degrade rather than throw.</summary>
    [Fact]
    public async Task Image_without_a_code_yields_a_warning_not_an_error()
    {
        using var blank = new SKBitmap(64, 64, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(blank)) canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(blank);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        var extractor = new ImageTicketExtractor(Decoder());
        var result = await extractor.ExtractAsync(encoded.ToArray(), TicketFileType.Png);

        Assert.Null(result.RawPayload);
        Assert.Empty(result.DetectedFields);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Undecodable_bytes_return_null_rather_than_throwing()
    {
        Assert.Null(Decoder().Decode([1, 2, 3, 4, 5]));
    }

    [Fact]
    public async Task Image_extractor_reports_photo_as_the_source()
    {
        var png = EncodeBarcodePng("ZAG-RJK", BarcodeFormat.QR_CODE);
        var extractor = new ImageTicketExtractor(Decoder());

        var result = await extractor.ExtractAsync(png, TicketFileType.Jpeg);

        Assert.Equal("ZAG-RJK", result.RawPayload);
        Assert.Equal(ImportSource.Photo, extractor.SourceFor(TicketFileType.Jpeg));
    }

    // ── Apple Wallet passes ─────────────────────────────────────────────────────────

    private static byte[] BuildPkPass(string passJson, params (string Name, string Content)[] extras)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("pass.json");
            using (var writer = new StreamWriter(entry.Open()))
                writer.Write(passJson);

            foreach (var (name, content) in extras)
            {
                var extra = archive.CreateEntry(name);
                using var extraWriter = new StreamWriter(extra.Open());
                extraWriter.Write(content);
            }
        }
        return buffer.ToArray();
    }

    private static PkPassTicketExtractor PkPass() => new(NullLogger<PkPassTicketExtractor>.Instance);

    [Fact]
    public async Task Reads_a_transit_boarding_pass_end_to_end()
    {
        var pass = BuildPkPass(JsonSerializer.Serialize(new
        {
            organizationName = "HŽ Putnički Prijevoz",
            description = "Zagreb to Rijeka",
            relevantDate = "2026-08-14T07:35:00Z",
            expirationDate = "2026-08-14T23:59:59Z",
            barcodes = new[] { new { message = "UIC918-ABC123", format = "PKBarcodeFormatAztec" } },
            boardingPass = new
            {
                transitType = "PKTransitTypeTrain",
                primaryFields = new[]
                {
                    new { key = "origin", label = "From", value = "Zagreb Gl. Kol." },
                    new { key = "destination", label = "To", value = "Rijeka" }
                },
                auxiliaryFields = new[] { new { key = "fare", label = "Price", value = "18,40 EUR" } }
            }
        }));

        var result = await PkPass().ExtractAsync(pass, TicketFileType.PkPass);

        Assert.Equal("HŽ Putnički Prijevoz", result.OperatorNameSnapshot);
        Assert.Equal("Zagreb Gl. Kol.", result.OriginName);
        Assert.Equal("Rijeka", result.DestinationName);
        Assert.Equal("UIC918-ABC123", result.RawPayload);
        Assert.Equal(TicketFormat.Barcode, result.PayloadFormat);
        Assert.Equal(18.40m, result.Price);
        Assert.Equal("EUR", result.Currency);
        Assert.Equal(new DateTime(2026, 8, 14, 7, 35, 0, DateTimeKind.Utc), result.ValidFrom);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task Reads_the_deprecated_singular_barcode_field()
    {
        var pass = BuildPkPass(JsonSerializer.Serialize(new
        {
            description = "Old issuer pass",
            barcode = new { message = "LEGACY-1", format = "PKBarcodeFormatQR" }
        }));

        var result = await PkPass().ExtractAsync(pass, TicketFileType.PkPass);

        Assert.Equal("LEGACY-1", result.RawPayload);
        Assert.Equal(TicketFormat.QR, result.PayloadFormat);
    }

    /// <summary>
    /// A pass stating when it becomes relevant but not when it expires would otherwise produce a
    /// ticket the expiry sweep never touches, since that sweep keys entirely off ValidTo.
    /// </summary>
    [Fact]
    public async Task Pass_without_an_expiry_still_gets_a_validity_end()
    {
        var pass = BuildPkPass(JsonSerializer.Serialize(new
        {
            description = "Day ticket",
            relevantDate = "2026-08-14T07:35:00Z"
        }));

        var result = await PkPass().ExtractAsync(pass, TicketFileType.PkPass);

        Assert.NotNull(result.ValidTo);
        Assert.Equal(new DateTime(2026, 8, 14), result.ValidTo!.Value.Date);
    }

    [Fact]
    public async Task Reads_event_ticket_style_passes_too()
    {
        var pass = BuildPkPass(JsonSerializer.Serialize(new
        {
            description = "Festival shuttle",
            eventTicket = new
            {
                primaryFields = new[] { new { key = "departure", label = "Depart", value = "Split" } },
                secondaryFields = new[] { new { key = "arrival", label = "Arrive", value = "Hvar" } }
            }
        }));

        var result = await PkPass().ExtractAsync(pass, TicketFileType.PkPass);

        Assert.Equal("Split", result.OriginName);
        Assert.Equal("Hvar", result.DestinationName);
    }

    /// <summary>ZIP magic bytes alone only prove "some archive".</summary>
    [Fact]
    public async Task An_archive_without_pass_json_is_rejected()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("definitely not a wallet pass");
        }

        var error = await Assert.ThrowsAsync<AppException>(
            () => PkPass().ExtractAsync(buffer.ToArray(), TicketFileType.PkPass));

        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task Corrupt_archive_bytes_are_rejected_cleanly()
    {
        byte[] notAZip = [0x50, 0x4B, 0x03, 0x04, .. new byte[64]];

        var error = await Assert.ThrowsAsync<AppException>(
            () => PkPass().ExtractAsync(notAZip, TicketFileType.PkPass));

        Assert.Equal(400, error.StatusCode);
    }

    /// <summary>
    /// The archive is attacker-controlled, so a few KB of upload must not be able to expand into
    /// gigabytes during extraction.
    /// </summary>
    [Fact]
    public async Task A_zip_bomb_is_rejected()
    {
        // Highly compressible: ~8 MB of zeros per entry, several entries, tiny on the wire.
        var payload = new string('\0', 8 * 1024 * 1024);
        var bomb = BuildPkPass(
            "{}",
            ("bomb1.bin", payload),
            ("bomb2.bin", payload),
            ("bomb3.bin", payload));

        var error = await Assert.ThrowsAsync<AppException>(
            () => PkPass().ExtractAsync(bomb, TicketFileType.PkPass));

        Assert.Equal(400, error.StatusCode);
        Assert.Contains("implausible", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Too_many_entries_is_rejected()
    {
        var extras = Enumerable.Range(0, 100).Select(i => ($"file{i}.txt", "x")).ToArray();
        var pass = BuildPkPass("{}", extras);

        await Assert.ThrowsAsync<AppException>(() => PkPass().ExtractAsync(pass, TicketFileType.PkPass));
    }

    // ── Calendar invites ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reads_a_calendar_invite()
    {
        var ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Test//EN
            BEGIN:VEVENT
            UID:booking-1@example.com
            SUMMARY:Zagreb → Vienna
            LOCATION:Zagreb Glavni Kolodvor
            DTSTART:20260814T063000Z
            DTEND:20260814T130000Z
            END:VEVENT
            END:VCALENDAR
            """;

        var extractor = new ICalTicketExtractor(NullLogger<ICalTicketExtractor>.Instance);
        var result = await extractor.ExtractAsync(Encoding.UTF8.GetBytes(ics), TicketFileType.ICalendar);

        Assert.Equal("Zagreb → Vienna", result.TicketName);
        Assert.Equal(new DateTime(2026, 8, 14, 6, 30, 0, DateTimeKind.Utc), result.ValidFrom);
        Assert.Equal(new DateTime(2026, 8, 14, 13, 0, 0, DateTimeKind.Utc), result.ValidTo);
        // The summary reads as a route, which is what journey grouping later chains on.
        Assert.Equal("Zagreb", result.OriginName);
        Assert.Equal("Vienna", result.DestinationName);
        Assert.Equal(ImportSource.Calendar, extractor.SourceFor(TicketFileType.ICalendar));
    }

    [Fact]
    public async Task A_calendar_with_no_events_is_rejected()
    {
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Test//EN\r\nEND:VCALENDAR";
        var extractor = new ICalTicketExtractor(NullLogger<ICalTicketExtractor>.Instance);

        await Assert.ThrowsAsync<AppException>(
            () => extractor.ExtractAsync(Encoding.UTF8.GetBytes(ics), TicketFileType.ICalendar));
    }
}
