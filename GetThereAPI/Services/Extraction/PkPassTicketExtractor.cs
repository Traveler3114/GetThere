using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

using GetThereAPI.Exceptions;

using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

using GetThereExtraction;

namespace GetThereAPI.Services.Extraction;

/// <summary>
/// Reads an Apple Wallet pass.
/// <para>
/// This is the best source the importer has. A <c>.pkpass</c> is a ZIP whose <c>pass.json</c>
/// carries the ticket as structured data — organiser, dates, origin and destination, and the
/// barcode payload already decoded — so nothing has to be guessed from prose or recovered from a
/// photograph. It is also the format airlines and rail operators actually email out.
/// </para>
/// </summary>
public class PkPassTicketExtractor : ITicketExtractor
{
    private readonly ILogger<PkPassTicketExtractor> _logger;

    public PkPassTicketExtractor(ILogger<PkPassTicketExtractor> logger) { _logger = logger; }

    public IReadOnlyCollection<TicketFileType> SupportedTypes { get; } = [TicketFileType.PkPass];

    public ImportSource SourceFor(TicketFileType fileType) => ImportSource.PkPass;

    // A pass is a handful of small JSON and PNG files. These bounds are far above anything
    // legitimate and exist because the archive is attacker-controlled: without them a few KB of
    // upload can expand into gigabytes of decompression. TransitInfoAPI guards GTFS zips the
    // same way.
    private const int MaxEntries = 64;
    private const long MaxEntrySize = 2 * 1024 * 1024;
    private const long MaxTotalSize = 16 * 1024 * 1024;
    private const long MaxCompressionRatio = 200;

    /// <summary>Field keys operators use for the two ends of a journey.</summary>
    private static readonly string[] OriginKeys = ["origin", "from", "depart", "departure", "boarding", "source"];
    private static readonly string[] DestinationKeys = ["destination", "to", "arrive", "arrival", "dest"];

    public Task<TicketExtractionResult> ExtractAsync(byte[] content, TicketFileType fileType, CancellationToken ct = default)
    {
        var result = new TicketExtractionResult();

        var passJson = ReadPassJson(content);
        if (passJson is null)
        {
            // ZIP magic bytes alone only prove "some archive". Without a pass.json this is not a
            // wallet pass, and there is nothing to import.
            throw new AppException("That file is not an Apple Wallet pass — no pass.json inside.", 400);
        }

        // Guarded like every other parse in this pipeline. Unhandled, a pass.json that is malformed
        // — or merely nested deeper than JsonDocument's default 64-level ceiling — escaped as a
        // JsonException and became a 500, on input the caller chooses.
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(passJson);
        }
        catch (JsonException ex)
        {
            _logger.LogInformation(ex, "Wallet pass contained an unreadable pass.json");
            throw new AppException("That wallet pass contains a pass.json we could not read.", 400);
        }

        using var _ = document;
        var root = document.RootElement;

        ReadTopLevel(root, result);
        ReadBarcode(root, result);
        ReadStyleFields(root, result);

        if (result.OriginName is not null && result.DestinationName is not null)
        {
            result.RouteDescription ??= $"{result.OriginName} → {result.DestinationName}";
            result.TicketName ??= result.RouteDescription;
        }

        if (result.DetectedFields.Count == 0)
        {
            result.Warning = "The pass was readable but carried no ticket fields we recognised.";
            _logger.LogInformation("Wallet pass parsed but yielded no recognised ticket fields");
        }

        return Task.FromResult(result);
    }

    /// <summary>Extracts pass.json under strict decompression bounds, or null if there is none.</summary>
    private static byte[]? ReadPassJson(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new AppException("That file could not be opened as an Apple Wallet pass.", 400);
        }

        using (archive)
        {
            if (archive.Entries.Count > MaxEntries)
                throw new AppException("That wallet pass has too many entries to be genuine.", 400);

            long total = 0;
            foreach (var entry in archive.Entries)
            {
                total += entry.Length;
                if (entry.Length > MaxEntrySize || total > MaxTotalSize)
                    throw new AppException("That wallet pass expands to an implausible size.", 400);

                if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxCompressionRatio)
                    throw new AppException("That wallet pass expands to an implausible size.", 400);
            }

            // Case-insensitive: the spec says pass.json, but archives in the wild are inconsistent.
            var passEntry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, "pass.json", StringComparison.OrdinalIgnoreCase));

            if (passEntry is null) return null;

            using var entryStream = passEntry.Open();
            using var buffer = new MemoryStream();
            // Bounded copy: entry.Length is metadata the archive declares about itself, so it is
            // checked above but not trusted as the actual read limit.
            CopyBounded(entryStream, buffer, MaxEntrySize);
            return buffer.ToArray();
        }
    }

    private static void CopyBounded(Stream source, Stream destination, long limit)
    {
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            written += read;
            if (written > limit)
                throw new AppException("That wallet pass expands to an implausible size.", 400);
            destination.Write(buffer, 0, read);
        }
    }

    private static void ReadTopLevel(JsonElement root, TicketExtractionResult result)
    {
        if (TryGetString(root, "organizationName", out var organiser))
        {
            result.OperatorNameSnapshot = organiser;
            result.DetectedFields.Add(nameof(result.OperatorNameSnapshot));
        }

        if (TryGetString(root, "description", out var description))
            result.TicketName = description;

        // relevantDate is when the pass matters — departure, for a transit ticket.
        if (TryGetDate(root, "relevantDate", out var relevant))
        {
            result.ValidFrom = relevant;
            result.DetectedFields.Add(nameof(result.ValidFrom));
        }

        if (TryGetDate(root, "expirationDate", out var expires))
        {
            result.ValidTo = expires;
            result.DetectedFields.Add(nameof(result.ValidTo));
        }

        // A pass with a start but no stated end is valid for that day; leaving ValidTo null would
        // exempt it from the expiry sweep entirely.
        if (result.ValidFrom is not null && result.ValidTo is null)
            result.ValidTo = result.ValidFrom.Value.Date.AddDays(1).AddTicks(-1);
    }

    private static void ReadBarcode(JsonElement root, TicketExtractionResult result)
    {
        // "barcodes" is the current array form; "barcode" is the deprecated singular still emitted
        // by older issuers.
        JsonElement barcode = default;
        var found = false;

        if (root.TryGetProperty("barcodes", out var barcodes)
            && barcodes.ValueKind == JsonValueKind.Array
            && barcodes.GetArrayLength() > 0)
        {
            barcode = barcodes[0];
            found = true;
        }
        else if (root.TryGetProperty("barcode", out var single) && single.ValueKind == JsonValueKind.Object)
        {
            barcode = single;
            found = true;
        }

        if (!found) return;

        if (TryGetString(barcode, "message", out var message))
        {
            result.RawPayload = message;
            result.PayloadFormat = BarcodeDecoder.FromPkPassFormat(
                TryGetString(barcode, "format", out var format) ? format : null);
            result.DetectedFields.Add(nameof(result.RawPayload));
        }
    }

    /// <summary>
    /// Walks the style-specific field groups. A transit pass is a boardingPass, but operators also
    /// issue eventTicket and generic passes for the same thing, so all five styles are read.
    /// </summary>
    private static void ReadStyleFields(JsonElement root, TicketExtractionResult result)
    {
        string[] styles = ["boardingPass", "eventTicket", "coupon", "generic", "storeCard"];
        string[] groups = ["primaryFields", "secondaryFields", "auxiliaryFields", "headerFields", "backFields"];

        foreach (var style in styles)
        {
            if (!root.TryGetProperty(style, out var styleElement) || styleElement.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var group in groups)
            {
                if (!styleElement.TryGetProperty(group, out var fields) || fields.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var field in fields.EnumerateArray())
                    ReadField(field, result);
            }
        }
    }

    private static void ReadField(JsonElement field, TicketExtractionResult result)
    {
        if (!TryGetString(field, "key", out var key)) return;
        if (!TryGetValueAsString(field, out var value) || string.IsNullOrWhiteSpace(value)) return;

        var label = TryGetString(field, "label", out var l) ? l : null;
        var haystack = $"{key} {label}".ToLowerInvariant();

        if (result.OriginName is null && OriginKeys.Any(haystack.Contains))
        {
            result.OriginName = value.Trim();
            result.DetectedFields.Add(nameof(result.OriginName));
            return;
        }

        if (result.DestinationName is null && DestinationKeys.Any(haystack.Contains))
        {
            result.DestinationName = value.Trim();
            result.DetectedFields.Add(nameof(result.DestinationName));
            return;
        }

        if (result.Price is null
            && (haystack.Contains("price") || haystack.Contains("fare") || haystack.Contains("amount"))
            && TryParseMoney(value, out var amount, out var currency))
        {
            result.Price = amount;
            result.Currency = currency;
            result.DetectedFields.Add(nameof(result.Price));
            result.DetectedFields.Add(nameof(result.Currency));
        }
    }

    /// <summary>
    /// Pass fields hold display strings ("€ 12,50", "12.50 EUR"), so the price is scraped rather
    /// than parsed, and only accepted when it lands on a currency this system supports.
    /// </summary>
    internal static bool TryParseMoney(string value, out decimal amount, out string? currency)
    {
        var scraped = new TicketExtractionResult();
        TicketTextScraper.Scrape(value, scraped);

        if (scraped.Price is not null && scraped.Currency is not null)
        {
            amount = scraped.Price.Value;
            currency = scraped.Currency;
            return true;
        }

        amount = 0;
        currency = null;
        return false;
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>A pass field's value may be a string, a number, or an ISO date.</summary>
    private static bool TryGetValueAsString(JsonElement element, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!element.TryGetProperty("value", out var property)) return false;

        value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            _ => string.Empty
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetDate(JsonElement element, string name, out DateTime value)
    {
        value = default;
        if (!TryGetString(element, name, out var raw)) return false;

        // W3C date-time per the pass spec; normalised to UTC here so the create path and the
        // expiry sweep see a consistent kind.
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return false;

        value = parsed.UtcDateTime;
        return true;
    }
}
