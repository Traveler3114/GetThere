using System.Text.Json;

using GetThereAPI.Services;

namespace GetThereAPI.Managers;

/// <summary>
/// What remains of the map proxy's logic.
/// <para>
/// This used to hold the whitelisted passthrough to TransitInfoAPI and typed, cached reads for
/// stations, routes, mobility docks and vehicles, all serving a map page GetThereAPI hosted. The
/// page now lives in TransitInfoAPI and the client loads it directly, which makes it same-origin
/// with its data — so the proxy, the allow-list that kept it from becoming an open gateway, and the
/// caching that absorbed a panning map all went with it.
/// </para>
/// <para>
/// One read is left, backing the admin console's reachability probe.
/// </para>
/// </summary>
public class MapManager
{
    /// <summary>Upstream path serving the transport-type list.</summary>
    private const string TransportTypesUpstreamPath = "/operators/types";

    private readonly TransitInfoApiClient _transitClient;

    public MapManager(TransitInfoApiClient transitClient)
    {
        _transitClient = transitClient;
    }

    /// <summary>
    /// Transport types and their icons, read from TransitInfoAPI's operators endpoint.
    /// <para>
    /// This asked for <c>/map/transport-types</c>, which does not exist upstream — TransitInfoAPI has
    /// no map controller at all — so every call answered 404 and this API turned that into a 502.
    /// The data lives on <c>/operators/types</c>.
    /// </para>
    /// </summary>
    public async Task<JsonElement> GetTransportTypesAsync(CancellationToken ct = default)
    {
        var (body, _) = await _transitClient.GetRawAsync(TransportTypesUpstreamPath, ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }
}
