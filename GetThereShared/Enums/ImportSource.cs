namespace GetThereShared.Enums;

/// <summary>
/// How a ticket got into the wallet. Everything other than <see cref="Manual"/> requires an
/// uploaded file, so those values are only accepted alongside a blob key from the upload endpoint.
/// </summary>
public enum ImportSource
{
    Manual,
    Photo,
    Pdf,
    QrScan,
    /// <summary>An Apple Wallet pass.</summary>
    PkPass,
    /// <summary>An iCalendar booking confirmation.</summary>
    Calendar,
    /// <summary>Pasted confirmation text, scraped for fields.</summary>
    Text
}
