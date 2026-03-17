using GetThereAPI.Entities;

namespace GetThereAPI.Parsers.Realtime;

/// <summary>
/// Returns the correct IStaticDataParser for an operator based on
/// TransitOperator.StaticFeedFormat stored in the database.
///
/// To add a new format:
///   1. Create YourFormatStaticParser : IStaticDataParser
///   2. Add a case below
///   3. Done — StaticDataManager needs no changes
/// </summary>
public static class StaticParserFactory
{
    // Singleton — parser is stateless so one instance is fine
    private static readonly GtfsStaticParser _gtfs = new();

    public static IStaticDataParser GetParser(TransitOperator op)
        => op.StaticFeedFormat switch
        {
            "GTFS" => _gtfs,
            _      => _gtfs    // GTFS is the universal default
        };
}
