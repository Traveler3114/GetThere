using System.Text;

using GetThereShared.Enums;

namespace GetThereShared.Extraction;

/// <summary>
/// Decides what an uploaded file actually is by reading its leading bytes.
/// <para>
/// The multipart <c>Content-Type</c> header and the filename are both chosen by the caller, so
/// neither can gate what the extraction pipeline will open. This is the allow-list: a format that
/// does not match one of these signatures is rejected outright, and the detected type — not the
/// declared one — selects the extractor.
/// </para>
/// </summary>
public static class TicketFileSniffer
{
    /// <summary>Bytes the caller must supply for <see cref="Detect"/> to reach every signature.</summary>
    public const int HeaderBytes = 32;

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();
    private static readonly byte[] Ftyp = "ftyp"u8.ToArray();
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Local ZIP entry header. An Apple Wallet pass is a ZIP, so this only narrows the file to
    /// "might be a pkpass" — the extractor still has to find a readable <c>pass.json</c> inside,
    /// which is what separates a real pass from any other archive.
    /// </summary>
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>ISO base-media brands that indicate HEIF/HEIC rather than, say, an MP4.</summary>
    private static readonly string[] HeicBrands = ["heic", "heix", "hevc", "hevx", "mif1", "msf1", "heim", "heis"];

    public static TicketFileType? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(Jpeg)) return TicketFileType.Jpeg;
        if (header.StartsWith(Png)) return TicketFileType.Png;
        if (header.StartsWith(Pdf)) return TicketFileType.Pdf;
        if (header.StartsWith(Zip)) return TicketFileType.PkPass;

        // RIFF containers carry the real format in bytes 8-11; WAVE and AVI share the prefix.
        if (header.Length >= 12 && header.StartsWith(Riff) && header[8..12].SequenceEqual(Webp))
            return TicketFileType.Webp;

        // ISO base media: a 4-byte box size, then "ftyp", then the brand.
        if (header.Length >= 12 && header[4..8].SequenceEqual(Ftyp))
        {
            var brand = Encoding.ASCII.GetString(header[8..12]);
            if (HeicBrands.Contains(brand, StringComparer.OrdinalIgnoreCase))
                return TicketFileType.Heic;
        }

        if (IsICalendar(header)) return TicketFileType.ICalendar;

        return null;
    }

    /// <summary>
    /// iCalendar is plain text with no binary signature, so it is identified by its mandatory
    /// opening line (RFC 5545 §3.4) after skipping a BOM and any leading whitespace.
    /// </summary>
    private static bool IsICalendar(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(Utf8Bom))
            header = header[3..];

        while (header.Length > 0 && (header[0] is 0x20 or 0x09 or 0x0D or 0x0A))
            header = header[1..];

        return header.StartsWith("BEGIN:VCALENDAR"u8);
    }

    public static string ContentTypeFor(TicketFileType type) => type switch
    {
        TicketFileType.Jpeg => "image/jpeg",
        TicketFileType.Png => "image/png",
        TicketFileType.Webp => "image/webp",
        TicketFileType.Heic => "image/heic",
        TicketFileType.Pdf => "application/pdf",
        TicketFileType.PkPass => "application/vnd.apple.pkpass",
        TicketFileType.ICalendar => "text/calendar",
        _ => "application/octet-stream"
    };

    /// <summary>
    /// The stored file's extension. Derived from the detected type rather than the uploaded
    /// filename, which never reaches the filesystem.
    /// </summary>
    public static string ExtensionFor(TicketFileType type) => type switch
    {
        TicketFileType.Jpeg => ".jpg",
        TicketFileType.Png => ".png",
        TicketFileType.Webp => ".webp",
        TicketFileType.Heic => ".heic",
        TicketFileType.Pdf => ".pdf",
        TicketFileType.PkPass => ".pkpass",
        TicketFileType.ICalendar => ".ics",
        _ => ".bin"
    };

    /// <summary>Human-readable allow-list, for the error a rejected upload gets back.</summary>
    public static string SupportedDescription =>
        "JPEG, PNG, WebP, HEIC, PDF, Apple Wallet pass (.pkpass), or iCalendar (.ics)";
}
