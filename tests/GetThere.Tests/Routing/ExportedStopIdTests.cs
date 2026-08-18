using TransitInfoAPI.Routing.Export;

namespace GetThere.Tests.Routing;

/// <summary>
/// The whole-map bundle merges every active feed version, but a raw stop id is unique only within a
/// version. These pin the namespacing that keeps the exported <c>stop_id</c> a valid GTFS primary key
/// and — critically — reversible, since GTFS-RT re-serve (Step 6) translates the operator's original
/// id back to the exported one by decoding this pair.
/// </summary>
public class ExportedStopIdTests
{
    [Fact]
    public void Two_versions_sharing_a_raw_stop_id_produce_distinct_exported_ids()
    {
        // The same operator stop id string "S1" imported under two feed versions.
        var a = ExportedStopId.Encode(1, "S1");
        var b = ExportedStopId.Encode(2, "S1");

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(1, "S1")]
    [InlineData(2, "S1")]
    [InlineData(42, "STOP:WITH:COLONS")] // original ids can themselves contain the delimiter
    [InlineData(7, "")]                   // and can be empty
    public void An_exported_id_round_trips_back_to_its_version_and_original_id(int feedVersionId, string rawStopId)
    {
        var encoded = ExportedStopId.Encode(feedVersionId, rawStopId);

        var ok = ExportedStopId.TryDecode(encoded, out var decodedVersion, out var decodedRaw);

        Assert.True(ok);
        Assert.Equal(feedVersionId, decodedVersion);
        Assert.Equal(rawStopId, decodedRaw);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HR-ZG-0001")]        // a bare canonical OnestopId, emitted un-namespaced
    [InlineData(":S1")]                // empty version prefix
    [InlineData("abc:S1")]             // non-integer version prefix
    public void A_non_namespaced_value_does_not_decode(string? value)
    {
        var ok = ExportedStopId.TryDecode(value, out _, out _);

        Assert.False(ok);
    }
}
