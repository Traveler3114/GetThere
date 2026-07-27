using System.Globalization;

namespace GetThereShared.Common;

/// <summary>
/// One place that turns an amount plus a currency code into display text.
/// <para>
/// Money was previously formatted two different ways, both wrong: <c>WalletTransactionResponse</c>
/// hardcoded a euro sign and ignored the currency entirely, and the MAUI balance was formatted with
/// a fixed <c>hr-HR</c> culture regardless of the user's language or the wallet's currency.
/// </para>
/// </summary>
public static class MoneyFormatter
{
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EUR"] = "€",
        ["USD"] = "$",
        ["GBP"] = "£",
        ["CHF"] = "CHF ",
        ["HRK"] = "kn "
    };

    /// <summary>Symbol for a currency code, falling back to the code itself plus a space.</summary>
    public static string SymbolFor(string? currency) =>
        currency is not null && Symbols.TryGetValue(currency, out var symbol)
            ? symbol
            : $"{currency ?? SupportedCurrencies.Default} ";

    /// <summary>
    /// Formats an amount for display. Grouping and the decimal separator follow
    /// <paramref name="culture"/> (the current UI culture when omitted), so the same value reads
    /// correctly in English and Croatian.
    /// </summary>
    public static string Format(decimal amount, string? currency, CultureInfo? culture = null) =>
        $"{SymbolFor(currency)}{amount.ToString("N2", culture ?? CultureInfo.CurrentCulture)}";

    /// <summary>Formats a signed amount with an explicit +/- prefix, for ledger rows.</summary>
    public static string FormatSigned(decimal amount, string? currency, CultureInfo? culture = null)
    {
        var sign = amount < 0 ? "-" : "+";
        return $"{sign}{Format(Math.Abs(amount), currency, culture)}";
    }
}
