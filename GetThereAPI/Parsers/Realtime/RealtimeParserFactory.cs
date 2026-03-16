using GetThere.Services.Realtime;
using GetThereShared.Dtos;

namespace GetThere.Helpers;

/// <summary>
/// Returns the correct IRealtimeParser for a given operator based on
/// TransitOperatorDto.RealtimeFeedFormat (set in the database).
/// </summary>
public static class RealtimeParserFactory
{
    // Singleton instances — parsers are stateless so one instance each is fine
    private static readonly GtfsRtProtoParser _proto = new();
    private static readonly GtfsRtJsonParser  _json  = new();
    private static readonly SiriParser        _siri  = new();
    private static readonly RestJsonParser    _rest  = new();

    public static IRealtimeParser GetParser(TransitOperatorDto op) =>
        op.RealtimeFeedFormat switch
        {
            "GTFS_RT_PROTO" => _proto,
            "GTFS_RT_JSON"  => _json,
            "SIRI"          => _siri,
            "REST"          => _rest,
            _ => _proto  // safe default
        };
}
