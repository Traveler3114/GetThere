using System.IO.Compression;
using System.Text;

using GetThereAPI.Common;

using GetThereShared.Enums;

namespace GetThere.Tests.ImportedTickets;

/// <summary>
/// The sniffer is the upload allow-list. Both the multipart <c>Content-Type</c> and the filename
/// are caller-supplied, so this is the only thing deciding what the extraction pipeline will open —
/// which makes "a PDF renamed .jpg" and "an archive that is not a wallet pass" the cases that
/// matter, not the happy path.
/// </summary>
public class TicketFileSnifferTests
{
    private static byte[] Header(params byte[] bytes)
    {
        // Padded to the length the sniffer asks callers for, so tests exercise the same slice the
        // upload path passes in.
        var buffer = new byte[TicketFileSniffer.HeaderBytes];
        bytes.CopyTo(buffer, 0);
        return buffer;
    }

    [Fact]
    public void Detects_jpeg()
    {
        Assert.Equal(TicketFileType.Jpeg, TicketFileSniffer.Detect(Header(0xFF, 0xD8, 0xFF, 0xE0)));
    }

    [Fact]
    public void Detects_png()
    {
        Assert.Equal(TicketFileType.Png, TicketFileSniffer.Detect(Header(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)));
    }

    [Fact]
    public void Detects_pdf()
    {
        Assert.Equal(TicketFileType.Pdf, TicketFileSniffer.Detect(Encoding.ASCII.GetBytes("%PDF-1.7\n%âãÏÓ")));
    }

    [Fact]
    public void Detects_zip_as_candidate_wallet_pass()
    {
        Assert.Equal(TicketFileType.PkPass, TicketFileSniffer.Detect(Header(0x50, 0x4B, 0x03, 0x04)));
    }

    [Fact]
    public void Detects_webp_but_not_other_riff_containers()
    {
        var webp = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");
        var wave = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt ");

        Assert.Equal(TicketFileType.Webp, TicketFileSniffer.Detect(webp));
        Assert.Null(TicketFileSniffer.Detect(wave));
    }

    [Theory]
    [InlineData("heic")]
    [InlineData("mif1")]
    [InlineData("heix")]
    public void Detects_heic_brands(string brand)
    {
        var bytes = new byte[] { 0, 0, 0, 0x18 }
            .Concat(Encoding.ASCII.GetBytes("ftyp"))
            .Concat(Encoding.ASCII.GetBytes(brand))
            .Concat(new byte[16])
            .ToArray();

        Assert.Equal(TicketFileType.Heic, TicketFileSniffer.Detect(bytes));
    }

    /// <summary>An MP4 shares the ftyp box but is not an image we can scan.</summary>
    [Fact]
    public void Rejects_non_image_iso_media()
    {
        var mp4 = new byte[] { 0, 0, 0, 0x18 }
            .Concat(Encoding.ASCII.GetBytes("ftyp"))
            .Concat(Encoding.ASCII.GetBytes("isom"))
            .Concat(new byte[16])
            .ToArray();

        Assert.Null(TicketFileSniffer.Detect(mp4));
    }

    [Fact]
    public void Detects_icalendar()
    {
        var ics = Encoding.UTF8.GetBytes("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n");
        Assert.Equal(TicketFileType.ICalendar, TicketFileSniffer.Detect(ics));
    }

    [Fact]
    public void Detects_icalendar_behind_a_bom_and_leading_whitespace()
    {
        var ics = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("\r\n  BEGIN:VCALENDAR\r\n"))
            .ToArray();

        Assert.Equal(TicketFileType.ICalendar, TicketFileSniffer.Detect(ics));
    }

    /// <summary>
    /// The whole point of sniffing: what the file claims to be is irrelevant. A PDF uploaded as
    /// image/jpeg with a .jpg name is still routed to the PDF extractor.
    /// </summary>
    [Fact]
    public void Ignores_what_the_caller_claims_the_file_is()
    {
        var actuallyPdf = Encoding.ASCII.GetBytes("%PDF-1.4 pretending to be a photo");
        Assert.Equal(TicketFileType.Pdf, TicketFileSniffer.Detect(actuallyPdf));
    }

    [Theory]
    [InlineData("")]
    [InlineData("just some text")]
    [InlineData("<html><body>nope</body></html>")]
    [InlineData("#!/bin/sh\nrm -rf /")]
    public void Rejects_everything_outside_the_allow_list(string content)
    {
        Assert.Null(TicketFileSniffer.Detect(Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void Rejects_a_truncated_header_without_throwing()
    {
        Assert.Null(TicketFileSniffer.Detect([0xFF]));
        Assert.Null(TicketFileSniffer.Detect([]));
        Assert.Null(TicketFileSniffer.Detect([0x52, 0x49, 0x46, 0x46]));
    }

    /// <summary>Every accepted type maps to a content type and an extension, or storage breaks.</summary>
    [Theory]
    [InlineData(TicketFileType.Jpeg, "image/jpeg", ".jpg")]
    [InlineData(TicketFileType.Png, "image/png", ".png")]
    [InlineData(TicketFileType.Pdf, "application/pdf", ".pdf")]
    [InlineData(TicketFileType.PkPass, "application/vnd.apple.pkpass", ".pkpass")]
    [InlineData(TicketFileType.ICalendar, "text/calendar", ".ics")]
    public void Maps_types_to_content_type_and_extension(TicketFileType type, string contentType, string extension)
    {
        Assert.Equal(contentType, TicketFileSniffer.ContentTypeFor(type));
        Assert.Equal(extension, TicketFileSniffer.ExtensionFor(type));
    }

    /// <summary>A real ZIP sniffs as a candidate pass; proving it is one is the extractor's job.</summary>
    [Fact]
    public void A_real_archive_sniffs_as_pkpass()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("whatever.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("not a pass");
        }

        Assert.Equal(TicketFileType.PkPass, TicketFileSniffer.Detect(buffer.ToArray()));
    }
}
