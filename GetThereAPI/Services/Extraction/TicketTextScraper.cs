using System.Globalization;
using System.Text.RegularExpressions;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThereAPI.Services.Extraction;

/// <summary>
/// Pulls ticket fields out of free text — the body of a PDF e-ticket, or a confirmation email the
/// user pasted in.
/// <para>
/// This is deliberately conservative. Operators have no common text layout, so anything found here
/// is a suggestion the user reviews; the scraper would rather leave a field blank than fill it
/// with something plausible and wrong. Only fields it is reasonably sure of are reported in
/// <see cref="TicketExtractionResult.DetectedFields"/>.
/// </para>
/// </summary>
public static partial class TicketTextScraper
{
    [GeneratedRegex(@"\b(\d{1,2})[./-](\d{1,2})[./-](\d{4})\b")]
    private static partial Regex DayFirstDate { get; }

    [GeneratedRegex(@"\b(\d{4})-(\d{1,2})-(\d{1,2})\b")]
    private static partial Regex IsoDate { get; }

    [GeneratedRegex(@"\b([01]?\d|2[0-3]):([0-5]\d)\b")]
    private static partial Regex TimeOfDay { get; }

    /// <summary>Amount then code (12,50 EUR) or code then amount (EUR 12.50).</summary>
    [GeneratedRegex(@"(?:(?<amount>\d+[.,]\d{2})\s*(?<code>EUR|USD|GBP|CHF|HRK)|(?<code2>EUR|USD|GBP|CHF|HRK|€|\$|£)\s*(?<amount2>\d+[.,]\d{2}))", RegexOptions.IgnoreCase)]
    private static partial Regex Money { get; }

    /// <summary>"Zagreb → Rijeka", "Zagreb - Rijeka", "Zagreb to Rijeka".</summary>
    [GeneratedRegex(@"^\s*(?<from>[\p{L}][\p{L}\s.'-]{1,48}?)\s*(?:→|->|–|—|\bto\b)\s*(?<to>[\p{L}][\p{L}\s.'-]{1,48}?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RouteLine { get; }

    [GeneratedRegex(@"\b(?:booking|reservation|reference|ref|pnr|order)\b\D{0,12}?(?<ref>[A-Z0-9]{5,12})\b", RegexOptions.IgnoreCase)]
    private static partial Regex BookingReference { get; }

    private static readonly Dictionary<string, string> SymbolToCode = new()
    {
        ["€"] = "EUR",
        ["$"] = "USD",
        ["£"] = "GBP"
    };

    /// <summary>Adds whatever can be read from <paramref name="text"/> to an existing result.</summary>
    public static void Scrape(string? text, TicketExtractionResult result)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        ScrapeRoute(lines, result);
        ScrapeMoney(text, result);
        ScrapeDates(text, result);

        if (result.TicketName is null)
        {
            var reference = BookingReference.Match(text);
            if (reference.Success)
            {
                result.TicketName = $"Booking {reference.Groups["ref"].Value}";
                result.DetectedFields.Add(nameof(result.TicketName));
            }
        }
    }

    private static void ScrapeRoute(string[] lines, TicketExtractionResult result)
    {
        if (result.OriginName is not null && result.DestinationName is not null) return;

        foreach (var line in lines)
        {
            // Long lines are prose, not a route header, and match the pattern far too eagerly.
            if (line.Length > 100) continue;

            var match = RouteLine.Match(line);
            if (!match.Success) continue;

            var from = match.Groups["from"].Value.Trim();
            var to = match.Groups["to"].Value.Trim();
            if (from.Length < 2 || to.Length < 2) continue;

            result.OriginName ??= from;
            result.DestinationName ??= to;
            result.RouteDescription ??= $"{from} → {to}";
            result.TicketName ??= $"{from} → {to}";
            result.DetectedFields.Add(nameof(result.OriginName));
            result.DetectedFields.Add(nameof(result.DestinationName));
            return;
        }
    }

    private static void ScrapeMoney(string text, TicketExtractionResult result)
    {
        if (result.Price is not null) return;

        foreach (Match match in Money.Matches(text))
        {
            var rawAmount = match.Groups["amount"].Success ? match.Groups["amount"].Value : match.Groups["amount2"].Value;
            var rawCode = match.Groups["code"].Success ? match.Groups["code"].Value : match.Groups["code2"].Value;

            if (!decimal.TryParse(rawAmount.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                continue;

            var code = SymbolToCode.TryGetValue(rawCode, out var mapped) ? mapped : rawCode.ToUpperInvariant();
            // Only currencies the rest of the system accepts, so extraction cannot produce a draft
            // the create endpoint would then reject.
            if (!SupportedCurrencies.All.Contains(code)) continue;

            result.Price = amount;
            result.Currency = code;
            result.DetectedFields.Add(nameof(result.Price));
            result.DetectedFields.Add(nameof(result.Currency));
            return;
        }
    }

    private static void ScrapeDates(string text, TicketExtractionResult result)
    {
        if (result.ValidFrom is not null) return;

        var dates = ParseDates(text).Distinct().OrderBy(d => d).ToList();
        if (dates.Count == 0) return;

        // Times are not tied back to a specific date here — pairing them reliably needs layout
        // information the text does not carry — so validity is expressed as whole days and the
        // user narrows it if they care.
        result.ValidFrom = dates[0];
        result.DetectedFields.Add(nameof(result.ValidFrom));

        var last = dates[^1];
        result.ValidTo = last > dates[0] ? last.AddDays(1).AddTicks(-1) : dates[0].AddDays(1).AddTicks(-1);
        result.DetectedFields.Add(nameof(result.ValidTo));
    }

    private static IEnumerable<DateTime> ParseDates(string text)
    {
        foreach (Match match in IsoDate.Matches(text))
        {
            if (TryBuild(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture), out var date))
                yield return date;
        }

        foreach (Match match in DayFirstDate.Matches(text))
        {
            // Day-first, matching the European operators this app targets. An ambiguous 03/04/2026
            // is read as 3 April; the user sees the parsed date in the form before saving.
            if (TryBuild(
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), out var date))
                yield return date;
        }
    }

    private static bool TryBuild(int year, int month, int day, out DateTime date)
    {
        date = default;
        // Reject anything outside a range a ticket could plausibly carry, so a version number or
        // an invoice code that happens to look like a date does not become a validity window.
        if (year < 2000 || year > 2100 || month is < 1 or > 12 || day is < 1 or > 31) return false;
        if (day > DateTime.DaysInMonth(year, month)) return false;

        date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        return true;
    }

    /// <summary>Exposed for the paste-text path, which has no other structure to lean on.</summary>
    public static bool LooksLikeTime(string text) => TimeOfDay.IsMatch(text);
}
