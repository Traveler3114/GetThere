namespace TransitInfoAPI.Enums;

/// <summary>Where a feed's data comes from, for the admin console and the import log.</summary>
public enum SourceProvenance
{
    /// <summary>The operator itself (or its official licensor) publishes this data.</summary>
    Official,

    /// <summary>A third-party mirror of an operator's data, without the operator's own blessing.</summary>
    UnofficialMirror,

    /// <summary>Reconstructed from observed behaviour (a timetabled route, a scanned ticket) — not published anywhere.</summary>
    ReverseEngineered
}