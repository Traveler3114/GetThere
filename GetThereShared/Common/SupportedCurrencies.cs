namespace GetThereShared.Common;

public static class SupportedCurrencies
{
    /// <summary>
    /// Currencies a new amount may be written in. Validation and the picker are driven from the
    /// same list, so the API cannot accept something the UI is unable to produce.
    /// </summary>
    public static readonly string[] All = ["EUR", "USD", "GBP", "CHF"];

    /// <summary>
    /// No longer writable, but still readable. HRK was retired in January 2023 when Croatia
    /// adopted the euro; rows predating that keep rendering through <see cref="MoneyFormatter"/>
    /// rather than being rewritten, because there is no conversion rate stored and silently
    /// restating what someone recorded paying would be worse than showing it as paid.
    /// </summary>
    public static readonly string[] Legacy = ["HRK"];

    /// <summary>What the currency picker offers.</summary>
    public static readonly string[] Selectable = ["EUR", "USD", "GBP", "CHF"];

    public const string Default = "EUR";

    /// <summary>True for a currency that can still be displayed, whether or not it can be written.</summary>
    public static bool IsKnown(string? code) =>
        code is not null
        && (All.Contains(code, StringComparer.OrdinalIgnoreCase)
            || Legacy.Contains(code, StringComparer.OrdinalIgnoreCase));
}
