using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThereShared.Extraction;

/// <summary>
/// Reads candidate ticket fields out of an uploaded file.
/// <para>
/// Extractors never create tickets. They produce a draft the user confirms, because what can be
/// read varies enormously by format — a wallet pass is structured data and yields near-complete
/// fields, whereas a photographed paper ticket may yield nothing but a barcode payload. Presenting
/// a guess as a saved ticket would put wrong data in someone's wallet silently.
/// </para>
/// </summary>
public interface ITicketExtractor
{
    /// <summary>File types this extractor handles.</summary>
    IReadOnlyCollection<TicketFileType> SupportedTypes { get; }

    /// <summary>The import source to record for tickets created from this file type.</summary>
    ImportSource SourceFor(TicketFileType fileType);

    Task<TicketExtractionResult> ExtractAsync(byte[] content, TicketFileType fileType, CancellationToken ct = default);
}

/// <summary>
/// Routes a sniffed file type to the extractor that understands it. Registered as a singleton over
/// all <see cref="ITicketExtractor"/> registrations, so adding a format is a DI line.
/// </summary>
public class TicketExtractorRegistry
{
    private readonly Dictionary<TicketFileType, ITicketExtractor> _byType = [];

    public TicketExtractorRegistry(IEnumerable<ITicketExtractor> extractors)
    {
        foreach (var extractor in extractors)
            foreach (var type in extractor.SupportedTypes)
                _byType[type] = extractor;
    }

    public ITicketExtractor? For(TicketFileType fileType) =>
        _byType.TryGetValue(fileType, out var extractor) ? extractor : null;
}
