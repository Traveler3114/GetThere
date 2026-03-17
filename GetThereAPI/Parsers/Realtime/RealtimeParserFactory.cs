using GetThereAPI.Entities;

namespace GetThereAPI.Parsers.Realtime;

/// <summary>
/// Returns the correct IRealtimeParser for an operator based on
/// TransitOperator.RealtimeFeedFormat stored in the database.
///
/// To add a new format:
///   1. Create NewFormatParser : IRealtimeParser
///   2. Add a case below
///   3. Done — RealtimeManager needs no changes
/// </summary>
public static class RealtimeParserFactory
{
    // Singletons — parsers are stateless so one instance per format is fine
    private static readonly GtfsRtProtoParser _proto = new();
    private static readonly GtfsRtJsonParser  _json  = new();
    private static readonly SiriParser        _siri  = new();
    private static readonly RestJsonParser    _rest  = new();

    public static IRealtimeParser GetParser(TransitOperator op)
        => op.RealtimeFeedFormat switch
        {
            "GTFS_RT_PROTO" => _proto,
            "GTFS_RT_JSON"  => _json,
            "SIRI"          => _siri,
            "REST"          => _rest,
            _               => _proto   // safe default
        };
}
