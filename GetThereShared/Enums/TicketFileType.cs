namespace GetThereShared.Enums;

/// <summary>
/// A file format the ticket importer accepts. Determined by sniffing the bytes, never by trusting
/// the uploaded <c>Content-Type</c> or file extension — both are caller-supplied.
/// </summary>
public enum TicketFileType
{
    Jpeg,
    Png,
    Webp,
    Heic,
    Pdf,
    /// <summary>An Apple Wallet pass: a ZIP whose <c>pass.json</c> carries the ticket fields.</summary>
    PkPass,
    /// <summary>An iCalendar file, typically a booking confirmation invite.</summary>
    ICalendar
}
