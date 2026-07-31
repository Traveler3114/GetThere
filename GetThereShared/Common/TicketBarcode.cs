using GetThereShared.Enums;

namespace GetThereShared.Common;

/// <summary>
/// Decides whether a ticket payload can be redrawn as a scannable code, and as which symbology.
/// <para>
/// Kept here, away from any encoder, for two reasons. It is a decision about ticket data rather than
/// about a drawing library, so it does not need one; and the MAUI project cannot be referenced by the
/// test project, so logic that lives there cannot be covered. This is the part most worth covering —
/// getting it wrong means a confident, wrong code at a gate.
/// </para>
/// </summary>
public static class TicketBarcode
{
    /// <summary>
    /// Longest payload still worth drawing as a linear barcode.
    /// <para>
    /// Code 128 has no hard limit, but density rises with length: past roughly this many characters
    /// the bars are narrower than a phone screen can render distinctly, and the symbol stops
    /// scanning long before the encoder complains. A bound here fails honestly instead.
    /// </para>
    /// </summary>
    public const int MaxLinearPayloadLength = 80;

    /// <summary>
    /// The symbology to draw <paramref name="payload"/> as, or null when nothing can be drawn
    /// safely.
    /// </summary>
    /// <remarks>
    /// Null is an ordinary answer, not an error. It covers a payload that is empty, a format that
    /// describes something other than a code (a stored document, an NFC credential, a booking
    /// reference), and — importantly — a payload that will not round-trip through the symbology its
    /// format implies. A UIC 918-3 rail ticket arrives as <see cref="TicketFormat.Barcode"/> because
    /// that is all the format enum can express, but its payload is compressed binary that Code 128
    /// cannot carry; encoding it anyway would yield a code that scans to the wrong bytes.
    /// </remarks>
    public static BarcodeSymbology? ChooseSymbology(TicketFormat format, string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        return format switch
        {
            TicketFormat.QR => BarcodeSymbology.QrCode,

            TicketFormat.Barcode when IsLinearEncodable(payload) => BarcodeSymbology.Code128,
            TicketFormat.Barcode => null,

            // Not codes: a file to open, a credential to tap, a reference to read out.
            TicketFormat.PDF or TicketFormat.NFC or TicketFormat.Reference => null,

            _ => null
        };
    }

    /// <summary>
    /// Whether every character sits inside Code 128's single-byte range and the payload is short
    /// enough to stay readable.
    /// </summary>
    public static bool IsLinearEncodable(string? payload) =>
        !string.IsNullOrEmpty(payload)
        && payload.Length <= MaxLinearPayloadLength
        && payload.All(c => c <= 0xFF);
}
