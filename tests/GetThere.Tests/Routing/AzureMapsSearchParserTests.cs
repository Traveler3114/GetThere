using TransitInfoAPI.Managers;

namespace GetThere.Tests.Routing;

/// <summary>
/// Geocoding is address → coordinates for a trip's endpoints. The parser mapping is pinned against a
/// recorded Azure Maps Search response so the field mapping (freeformAddress, position) is verifiable
/// without a subscription key or network.
/// </summary>
public class AzureMapsSearchParserTests
{
    private const string IlicaResponse = """
    {
      "summary": { "query": "ilica 242" },
      "results": [
        {
          "type": "Point Address",
          "score": 0.95,
          "address": { "freeformAddress": "Ilica 242, 10000 Zagreb", "countryCode": "HR" },
          "position": { "lat": 45.8103, "lon": 15.9421 }
        },
        {
          "type": "Street",
          "score": 0.71,
          "address": { "freeformAddress": "Ilica, 10000 Zagreb", "countryCode": "HR" },
          "position": { "lat": 45.8108, "lon": 15.9500 }
        }
      ]
    }
    """;

    [Fact]
    public void Parses_address_candidates_with_coordinates()
    {
        var results = AzureMapsSearchParser.Parse(IlicaResponse);

        Assert.Equal(2, results.Count);
        var top = results[0];
        Assert.Equal("Ilica 242, 10000 Zagreb", top.Label);
        Assert.Equal(45.8103, top.Lat);
        Assert.Equal(15.9421, top.Lon);
        Assert.Equal(0.95, top.Score);
        Assert.Equal("HR", top.CountryCode);
    }

    [Fact]
    public void Empty_results_parse_to_empty_list()
    {
        Assert.Empty(AzureMapsSearchParser.Parse("""{"summary":{},"results":[]}"""));
        Assert.Empty(AzureMapsSearchParser.Parse("""{"summary":{}}"""));
    }
}
