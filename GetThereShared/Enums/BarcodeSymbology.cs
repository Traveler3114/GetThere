namespace GetThereShared.Enums;

/// <summary>
/// A symbology a ticket payload can safely be drawn as.
/// <para>
/// Deliberately narrower than the set the server can <em>decode</em>. Import reads QR, Aztec,
/// PDF417, DataMatrix, Code 128/39, EAN-13 and ITF, but records the result as a
/// <see cref="TicketFormat"/>, which has only five values — so Aztec and PDF417 both come back as
/// <see cref="TicketFormat.Barcode"/> and DataMatrix comes back as <see cref="TicketFormat.QR"/>.
/// That information is gone by the time a client wants to redraw the code, and guessing wrong
/// produces something a barrier rejects. This enum therefore lists only what can be chosen with
/// confidence; everything else is "no code", and the caller shows the payload as text.
/// </para>
/// </summary>
public enum BarcodeSymbology
{
    QrCode,
    Code128
}
