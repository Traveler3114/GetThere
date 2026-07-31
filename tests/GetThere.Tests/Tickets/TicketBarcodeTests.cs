using GetThereShared.Common;
using GetThereShared.Enums;

namespace GetThere.Tests.Tickets;

/// <summary>
/// Which symbology a ticket payload may be redrawn as.
/// <para>
/// This is the highest-consequence pure decision in the client: get it wrong and the app shows a
/// confident, scannable, <em>incorrect</em> code at a barrier, which is worse than showing nothing.
/// The rendering itself needs a device and a real scanner, but the choice does not, so it lives in
/// GetThereShared where it can be pinned down here.
/// </para>
/// </summary>
public class TicketBarcodeTests
{
    private const string ShortPayload = "TKT-12345";

    [Fact]
    public void A_qr_payload_is_drawn_as_a_qr_code()
    {
        Assert.Equal(BarcodeSymbology.QrCode, TicketBarcode.ChooseSymbology(TicketFormat.QR, ShortPayload));
    }

    [Fact]
    public void A_short_single_byte_barcode_payload_is_drawn_as_code_128()
    {
        Assert.Equal(BarcodeSymbology.Code128, TicketBarcode.ChooseSymbology(TicketFormat.Barcode, ShortPayload));
    }

    [Theory]
    // None of these describe a code: a stored document, a tap credential, a reference to read out.
    [InlineData(TicketFormat.PDF)]
    [InlineData(TicketFormat.NFC)]
    [InlineData(TicketFormat.Reference)]
    public void Formats_that_are_not_codes_are_not_drawn(TicketFormat format)
    {
        Assert.Null(TicketBarcode.ChooseSymbology(format, ShortPayload));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_payload_is_not_drawn(string? payload)
    {
        Assert.Null(TicketBarcode.ChooseSymbology(TicketFormat.QR, payload));
        Assert.Null(TicketBarcode.ChooseSymbology(TicketFormat.Barcode, payload));
    }

    // ── The lossy-format trap ────────────────────────────────────────────────────
    // Import decodes Aztec and PDF417 — the formats UIC 918-3 rail tickets use — but TicketFormat
    // has no value for either, so both are stored as Barcode. Their payloads are compressed binary.
    // Drawing one as Code 128 would produce a symbol that scans to the wrong bytes, so the rule is
    // to refuse rather than guess.

    [Fact]
    public void A_binary_rail_payload_stored_as_Barcode_is_refused_rather_than_mis_encoded()
    {
        // Characters above U+00FF cannot survive Code 128's single-byte encoding.
        var binaryish = "#UT01" + new string((char)0x2603, 40);

        Assert.False(TicketBarcode.IsLinearEncodable(binaryish));
        Assert.Null(TicketBarcode.ChooseSymbology(TicketFormat.Barcode, binaryish));
    }

    [Fact]
    public void An_over_long_barcode_payload_is_refused()
    {
        // Not an encoder limit — a legibility one. Past this the bars are narrower than a phone
        // screen renders distinctly and the symbol stops scanning while still being "valid".
        var tooLong = new string('A', TicketBarcode.MaxLinearPayloadLength + 1);

        Assert.False(TicketBarcode.IsLinearEncodable(tooLong));
        Assert.Null(TicketBarcode.ChooseSymbology(TicketFormat.Barcode, tooLong));
    }

    [Fact]
    public void A_payload_at_the_length_limit_is_still_drawn()
    {
        var atLimit = new string('A', TicketBarcode.MaxLinearPayloadLength);

        Assert.True(TicketBarcode.IsLinearEncodable(atLimit));
        Assert.Equal(BarcodeSymbology.Code128, TicketBarcode.ChooseSymbology(TicketFormat.Barcode, atLimit));
    }

    [Fact]
    public void Length_and_range_limits_do_not_apply_to_qr()
    {
        // QR carries binary and far more of it, so neither bound has any business gating it — a long
        // UTF-8 payload that Code 128 must refuse is perfectly ordinary here.
        var long2D = new string((char)0x2603, TicketBarcode.MaxLinearPayloadLength * 4);

        Assert.Equal(BarcodeSymbology.QrCode, TicketBarcode.ChooseSymbology(TicketFormat.QR, long2D));
    }
}
