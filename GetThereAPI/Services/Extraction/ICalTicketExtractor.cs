using System.Text;

using GetThereAPI.Exceptions;

using GetThereShared.Contracts;
using GetThereShared.Enums;
using GetThereShared.Extraction;

using Ical.Net.CalendarComponents;

namespace GetThereAPI.Services.Extraction;

/// <summary>
/// Reads a calendar invite. Operators routinely attach one to a booking confirmation, and it is
/// the only common format that states the journey window explicitly rather than leaving it to be
/// inferred from prose.
/// </summary>
public class ICalTicketExtractor : ITicketExtractor
{
    private readonly ILogger<ICalTicketExtractor> _logger;

    public ICalTicketExtractor(ILogger<ICalTicketExtractor> logger) { _logger = logger; }

    public IReadOnlyCollection<TicketFileType> SupportedTypes { get; } = [TicketFileType.ICalendar];

    public ImportSource SourceFor(TicketFileType fileType) => ImportSource.Calendar;

    public Task<TicketExtractionResult> ExtractAsync(byte[] content, TicketFileType fileType, CancellationToken ct = default)
    {
        var result = new TicketExtractionResult();

        CalendarEvent? calendarEvent;
        try
        {
            var calendar = Ical.Net.Calendar.Load(Encoding.UTF8.GetString(content));
            // The earliest event: a confirmation may carry the outbound leg plus reminders, and
            // the journey itself is the one that starts first.
            calendarEvent = calendar?.Events.OrderBy(e => e.DtStart?.AsUtc ?? DateTime.MaxValue).FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Calendar file could not be parsed for ticket extraction");
            throw new AppException("That calendar file could not be read.", 400);
        }

        if (calendarEvent is null)
            throw new AppException("That calendar file contains no events to import.", 400);

        if (!string.IsNullOrWhiteSpace(calendarEvent.Summary))
        {
            result.TicketName = calendarEvent.Summary.Trim();
            result.DetectedFields.Add(nameof(result.TicketName));
        }

        if (!string.IsNullOrWhiteSpace(calendarEvent.Location))
        {
            result.RouteDescription = calendarEvent.Location.Trim();
            result.DetectedFields.Add(nameof(result.RouteDescription));
        }

        if (calendarEvent.DtStart is not null)
        {
            result.ValidFrom = calendarEvent.DtStart.AsUtc;
            result.DetectedFields.Add(nameof(result.ValidFrom));
        }

        if (calendarEvent.DtEnd is not null)
        {
            result.ValidTo = calendarEvent.DtEnd.AsUtc;
            result.DetectedFields.Add(nameof(result.ValidTo));
        }
        else if (result.ValidFrom is not null)
        {
            // An event with no end would otherwise produce a ticket the expiry sweep never
            // touches, since that sweep keys entirely off ValidTo.
            result.ValidTo = result.ValidFrom.Value.Date.AddDays(1).AddTicks(-1);
        }

        // Summary and location often read "Zagreb → Rijeka", which gives the structured endpoints
        // journey grouping needs; description may carry the price and booking reference.
        TicketTextScraper.Scrape(
            string.Join('\n', calendarEvent.Summary, calendarEvent.Location, calendarEvent.Description),
            result);

        return Task.FromResult(result);
    }
}
