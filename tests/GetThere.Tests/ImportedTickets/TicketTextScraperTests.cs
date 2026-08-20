using GetThereShared.Contracts;
using GetThereShared.Extraction;

namespace GetThere.Tests.ImportedTickets;

/// <summary>
/// The free-text scraper behind the PDF and paste-a-confirmation paths. These cases come from a real
/// FlixBus boarding pass whose date, route and booking number the earlier scraper all missed —
/// month-name dates, a plain-hyphen route, and a space-separated booking number.
/// </summary>
public class TicketTextScraperTests
{
    private static TicketExtractionResult Scrape(string text)
    {
        var result = new TicketExtractionResult();
        TicketTextScraper.Scrape(text, result);
        return result;
    }

    // ── Dates ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Reads_a_day_month_name_year_date()
    {
        var result = Scrape("Departure\nSaturday, 15 Aug 2026\n08:55");

        Assert.Equal(new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), result.ValidFrom);
        Assert.Contains(nameof(result.ValidFrom), result.DetectedFields);
    }

    [Fact]
    public void Reads_a_full_month_name_date()
    {
        var result = Scrape("Travel: 15 August 2026");

        Assert.Equal(new DateTime(2026, 8, 15).Date, result.ValidFrom!.Value.Date);
    }

    [Fact]
    public void Reads_a_month_first_date()
    {
        var result = Scrape("Travel date: Aug 15, 2026");

        Assert.Equal(new DateTime(2026, 8, 15).Date, result.ValidFrom!.Value.Date);
    }

    // ── Route ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Reads_a_hyphen_separated_route()
    {
        var result = Scrape("Sukosan - Zagreb");

        Assert.Equal("Sukosan", result.OriginName);
        Assert.Equal("Zagreb", result.DestinationName);
        Assert.Contains(nameof(result.OriginName), result.DetectedFields);
    }

    /// <summary>A hyphenated word on its own line is not a route — the hyphen needs surrounding spaces.</summary>
    [Fact]
    public void A_hyphenated_word_is_not_a_route()
    {
        var result = Scrape("Zagreb-based");

        Assert.Null(result.OriginName);
        Assert.Null(result.DestinationName);
    }

    // ── Booking reference ─────────────────────────────────────────────────────────────

    [Fact]
    public void Reads_a_space_separated_booking_number()
    {
        var result = Scrape("BOOKING NUMBER\n338 350 5281");

        Assert.Equal("Booking 3383505281", result.TicketName);
        Assert.Contains(nameof(result.TicketName), result.DetectedFields);
    }

    /// <summary>The label word after the keyword carries no digit, so it must not be taken as the reference.</summary>
    [Fact]
    public void The_label_word_after_the_keyword_is_not_the_reference()
    {
        var result = Scrape("BOOKING NUMBER\nNMBRONLY");

        Assert.Null(result.TicketName);
    }
}
