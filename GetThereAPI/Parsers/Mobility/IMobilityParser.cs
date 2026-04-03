using GetThereAPI.Dtos;
using GetThereAPI.Entities;

namespace GetThereAPI.Parsers.Mobility;

/// <summary>
/// Implemented by each mobility-provider adapter.
/// The implementor is responsible for fetching and parsing station data
/// using the configuration stored in <paramref name="provider"/>.
/// </summary>
public interface IMobilityParser
{
    /// <summary>
    /// Fetch live station data from the provider feed and return a normalised list.
    /// </summary>
    /// <param name="provider">The provider entity containing ApiBaseUrl, ApiKey, AdapterConfig etc.</param>
    /// <param name="http">A shared <see cref="HttpClient"/> the parser may use for HTTP calls.</param>
    Task<List<BikeStationDto>> ParseStationsAsync(MobilityProvider provider, HttpClient http);
}
