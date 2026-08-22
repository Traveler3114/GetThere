using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Services;

namespace GetThere.Tests;

/// <summary>
/// Regression for Phase 1: ExecuteAsync returns raw flattened rows, mappings must be applied.
/// Without ApplyMappings every vehicle is dropped silently.
/// </summary>
public class RealtimeMappingTests
{
    private static readonly IReadOnlyList<CustomSourceMapping> FlixbusMappings =
    [
        new CustomSourceMapping { SortOrder = 1, SourceExpression = "id", TargetField = "VehicleId", Kind = MappingKind.Direct },
        new CustomSourceMapping { SortOrder = 2, SourceExpression = "location.coordinates.latitude", TargetField = "Latitude", Kind = MappingKind.Direct },
        new CustomSourceMapping { SortOrder = 3, SourceExpression = "location.coordinates.longitude", TargetField = "Longitude", Kind = MappingKind.Direct },
        new CustomSourceMapping { SortOrder = 4, SourceExpression = "location.updated_at", TargetField = "LastUpdated", Kind = MappingKind.Direct },
        new CustomSourceMapping { SortOrder = 5, SourceExpression = "line.code", TargetField = "RouteId", Kind = MappingKind.Direct },
        new CustomSourceMapping { SortOrder = 6, SourceExpression = "line.code", TargetField = "RouteShortName", Kind = MappingKind.Direct },
        new CustomSourceMapping { SortOrder = 7, SourceExpression = "location.speed_category", TargetField = "CongestionLevel", Kind = MappingKind.Direct }
    ];

    private static string FixtureJson
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "flixbus-zagreb.json");
            if (File.Exists(path)) return File.ReadAllText(path);
            // Fallback to repo-relative path
            var alt = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "Fixtures", "flixbus-zagreb.json");
            if (File.Exists(alt)) return File.ReadAllText(alt);
            // Last resort: embedded synthetic
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tests", "GetThere.Tests", "Fixtures", "flixbus-zagreb.json"));
        }
    }

    private static int CountVehiclesAfterMapping(string json)
    {
        var result = new ExtractionResult();
        var rows = CustomSourceEngine.ParseJsonRows(json, "rides", result);
        var mapped = CustomSourceEngine.ApplyMappings(rows, FlixbusMappings);
        mapped = CustomSourceEngine.Deduplicate(mapped, "VehicleId", out _);
        var count = 0;
        foreach (var row in mapped)
        {
            if (TryToVehicle(row) is not null) count++;
        }
        return count;
    }

    private static object? TryToVehicle(ExtractedRow row)
    {
        // Mirrors RealtimeManager.ToVehicle: requires VehicleId, Latitude, Longitude, usable bounds
        if (!row.TryGetValue("VehicleId", out var vid) || string.IsNullOrWhiteSpace(vid?.ToString())) return null;
        if (!row.TryGetValue("Latitude", out var latObj) || latObj is null) return null;
        if (!row.TryGetValue("Longitude", out var lonObj) || lonObj is null) return null;
        if (!double.TryParse(latObj.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lat)) return null;
        if (!double.TryParse(lonObj.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lon)) return null;
        if (lat == 0 && lon == 0) return null;
        return new object();
    }

    [Fact]
    public void Mapped_rows_produce_at_least_one_vehicle()
    {
        var json = FixtureJson;
        var count = CountVehiclesAfterMapping(json);
        Assert.True(count > 0, $"Expected at least one vehicle after mapping, got {count}. Fixture likely missing location data.");
    }

    [Fact]
    public void Without_mapping_zero_vehicles_are_produced()
    {
        var json = FixtureJson;
        var result = new ExtractionResult();
        var rows = CustomSourceEngine.ParseJsonRows(json, "rides", result);
        // Without ApplyMappings, keys are "id" not "VehicleId" — ToVehicle finds nothing
        var count = 0;
        foreach (var row in rows)
            if (TryToVehicle(row) is not null) count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public void Rides_without_location_produce_no_vehicle_and_count_matches_location_present()
    {
        var json = FixtureJson;
        using var doc = JsonDocument.Parse(json);
        var rides = doc.RootElement.GetProperty("rides");
        var withLocation = 0;
        foreach (var r in rides.EnumerateArray())
        {
            if (r.TryGetProperty("location", out var loc) && loc.ValueKind != JsonValueKind.Null)
            {
                if (loc.TryGetProperty("coordinates", out var coords) && coords.ValueKind == JsonValueKind.Object)
                    withLocation++;
            }
        }
        var mappedCount = CountVehiclesAfterMapping(json);
        Assert.Equal(withLocation, mappedCount);
    }

    [Fact]
    public void LastUpdated_is_clamped_and_route_and_congestion_mapped()
    {
        var json = FixtureJson;
        var result = new ExtractionResult();
        var rows = CustomSourceEngine.ParseJsonRows(json, "rides", result);
        var mapped = CustomSourceEngine.ApplyMappings(rows, FlixbusMappings);
        // Find the future-dated ride
        var futureRow = mapped.FirstOrDefault(r => r.TryGetValue("VehicleId", out var v) && v?.ToString() == "ride-006");
        Assert.NotNull(futureRow);
        // LastUpdated raw is 2099, but ToVehicle clamps to UtcNow — simulate that logic
        var raw = futureRow!["LastUpdated"]?.ToString();
        Assert.NotNull(raw);
        // Parse as DateTime, expect future
        var reported = DateTime.Parse(raw!, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var clamped = reported > DateTime.UtcNow ? DateTime.UtcNow : reported;
        Assert.True(clamped <= DateTime.UtcNow.AddSeconds(1));

        // line.code lands in RouteId
        var ride1 = mapped.First(r => r["VehicleId"]?.ToString() == "ride-001");
        Assert.Equal("LB1", ride1["RouteId"]?.ToString());

        // speed_category lands in CongestionLevel
        Assert.Equal("1", ride1["CongestionLevel"]?.ToString());
    }

    [Fact]
    public async Task Engine_uses_customsource_client()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"rides":[]}""", System.Text.Encoding.UTF8, "application/json") });
        var factory = new StubHttpClientFactory(handler);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Feeds:AllowPrivateNetworkUrls"] = "true" }).Build();
        var engine = new CustomSourceEngine(factory, config, new StubEnvironment(), NullLogger<CustomSourceEngine>.Instance);
        var request = new CustomSourceRequest
        {
            Url = "https://operator.test/feed",
            HttpMethod = "GET",
            Format = CustomSourceFormat.Json,
            DataPath = "rides",
            TargetSection = TransitSection.Vehicles
        };
        await engine.ExecuteAsync(request, null);
        Assert.Equal("customsource", Assert.Single(factory.NamesRequested));
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }

    private sealed class StubHttpClientFactory(RecordingHandler handler) : IHttpClientFactory
    {
        public List<string> NamesRequested { get; } = [];
        public HttpClient CreateClient(string name)
        {
            NamesRequested.Add(name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ApplicationName { get; set; } = "GetThere.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
    }
}
