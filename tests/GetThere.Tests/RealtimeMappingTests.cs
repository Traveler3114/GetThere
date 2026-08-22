using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using TransitInfoAPI.Entities;
using TransitInfoAPI.Enums;
using TransitInfoAPI.Managers;
using TransitInfoAPI.Services;

namespace GetThere.Tests;

/// <summary>
/// Regression for the defect that made the realtime custom-source path produce nothing.
/// <para>
/// <see cref="CustomSourceEngine.ExecuteAsync"/> returns raw flattened rows; it does not map them.
/// The realtime path read them directly, so <see cref="RealtimeManager.ToVehicle"/> looked for
/// <c>VehicleId</c> in a row keyed <c>id</c> and dropped every vehicle. It was silent because a null
/// return from <c>ToVehicle</c> is also the ordinary "this ride has no GPS yet" case.
/// </para>
/// <para>
/// These call the real <c>ToVehicle</c> rather than a copy of its logic, against a real captured
/// FlixBus payload — a reimplementation here would pass whatever the production code did.
/// </para>
/// <para>
/// Scope, stated plainly: these pin the <em>contract</em> — raw rows yield nothing, mapped rows yield
/// vehicles — not the call site. They build the pipeline themselves, so removing
/// <c>ApplyMappings</c> from <c>RealtimeManager.PollCustomRealtimeAsync</c> again would not fail
/// them. Standing that method up needs a manager, a scope factory, an engine and a database;
/// covering it is the <c>GET /realtime/vehicles?feedId=flixbus-2</c> check in the runbook.
/// </para>
/// </summary>
public class RealtimeMappingTests
{
    /// <summary>The mappings TransitDataSeeder actually seeds, so a change there fails these.</summary>
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

    private static string FixtureJson =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "flixbus-zagreb.json"));

    /// <summary>The real pipeline: parse, map, dedupe, convert.</summary>
    private static List<TransitInfoAPI.Contracts.VehicleResponse> RunPipeline(string json)
    {
        var result = new ExtractionResult();
        var rows = CustomSourceEngine.ParseJsonRows(json, "rides", result);
        var mapped = CustomSourceEngine.ApplyMappings(rows, FlixbusMappings);
        mapped = CustomSourceEngine.Deduplicate(mapped, "VehicleId", out _);

        var vehicles = new List<TransitInfoAPI.Contracts.VehicleResponse>();
        foreach (var row in mapped)
        {
            var vehicle = RealtimeManager.ToVehicle(row, "flixbus-2");
            if (vehicle is not null) vehicles.Add(vehicle);
        }
        return vehicles;
    }

    /// <summary>Rides carrying coordinates, counted straight off the JSON rather than via the pipeline.</summary>
    private static int RidesWithCoordinates(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var count = 0;
        foreach (var ride in doc.RootElement.GetProperty("rides").EnumerateArray())
        {
            if (ride.TryGetProperty("location", out var location)
                && location.ValueKind == JsonValueKind.Object
                && location.TryGetProperty("coordinates", out var coords)
                && coords.ValueKind == JsonValueKind.Object)
            {
                count++;
            }
        }
        return count;
    }

    [Fact]
    public void Fixture_is_a_real_payload_with_positions()
    {
        // Guards the tests below: a fixture with no positions would make them vacuously true.
        var json = FixtureJson;
        using var doc = JsonDocument.Parse(json);
        Assert.NotEmpty(doc.RootElement.GetProperty("rides").EnumerateArray());
        Assert.True(RidesWithCoordinates(json) > 0, "Fixture carries no ride with coordinates — recapture it.");
    }

    [Fact]
    public void Mapped_rows_produce_vehicles()
    {
        var vehicles = RunPipeline(FixtureJson);
        Assert.NotEmpty(vehicles);
    }

    [Fact]
    public void Vehicle_count_matches_rides_carrying_coordinates()
    {
        var json = FixtureJson;
        Assert.Equal(RidesWithCoordinates(json), RunPipeline(json).Count);
    }

    [Fact]
    public void Without_mapping_no_vehicle_is_produced()
    {
        // The defect itself. Raw rows are keyed "id" and "location.coordinates.latitude", so every
        // lookup in ToVehicle misses. If this ever produces a vehicle, the row shape changed and the
        // test above is no longer proving anything.
        var result = new ExtractionResult();
        var rows = CustomSourceEngine.ParseJsonRows(FixtureJson, "rides", result);

        var produced = rows.Count(row => RealtimeManager.ToVehicle(row, "flixbus-2") is not null);

        Assert.Equal(0, produced);
    }

    [Fact]
    public void Line_code_and_speed_category_reach_the_vehicle()
    {
        var vehicles = RunPipeline(FixtureJson);
        var vehicle = Assert.Single(vehicles, v => v.VehicleId == "edf779de-d78c-40db-973b-48912198e914");

        Assert.Equal("N986", vehicle.RouteId);
        Assert.Equal("N986", vehicle.RouteShortName);
        Assert.Equal("STATIONARY", vehicle.CongestionLevel);
        Assert.Equal(45.804703, vehicle.Latitude, 6);
        Assert.Equal(15.991678, vehicle.Longitude, 6);
        Assert.True(vehicle.IsRealtime);
    }

    [Fact]
    public void Future_timestamps_are_clamped_to_now()
    {
        // Built by hand: the captured payload has no future-dated ride, and the clamp is exactly the
        // case a real capture cannot be relied on to contain. An unclamped entry is never evicted by
        // the stale-vehicle sweep, and the cache key includes an operator-supplied id.
        var row = new ExtractedRow
        {
            ["VehicleId"] = "future-1",
            ["Latitude"] = 45.8,
            ["Longitude"] = 15.99,
            ["LastUpdated"] = "2099-01-01T00:00:00Z"
        };

        var vehicle = RealtimeManager.ToVehicle(row, "flixbus-2");

        Assert.NotNull(vehicle);
        Assert.True(vehicle!.LastUpdated <= DateTime.UtcNow.AddSeconds(1),
            $"LastUpdated {vehicle.LastUpdated:O} was not clamped to now.");
    }

    [Fact]
    public void A_row_without_coordinates_produces_no_vehicle()
    {
        var row = new ExtractedRow { ["VehicleId"] = "not-moving-yet" };
        Assert.Null(RealtimeManager.ToVehicle(row, "flixbus-2"));
    }

    [Fact]
    public async Task Engine_uses_the_customsource_client()
    {
        // Pinned deliberately: the "gtfs" client has AllowAutoRedirect on, so asking for the wrong
        // name silently reinstates redirect-following for a credential-bearing request.
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"rides":[]}""", System.Text.Encoding.UTF8, "application/json")
        });
        var factory = new StubHttpClientFactory(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Feeds:AllowPrivateNetworkUrls"] = "true" })
            .Build();
        var engine = new CustomSourceEngine(factory, config, new StubEnvironment(), NullLogger<CustomSourceEngine>.Instance);

        await engine.ExecuteAsync(new CustomSourceRequest
        {
            Url = "https://operator.test/feed",
            HttpMethod = "GET",
            Format = CustomSourceFormat.Json,
            DataPath = "rides",
            TargetSection = TransitSection.Vehicles
        }, null);

        Assert.Equal("customsource", Assert.Single(factory.NamesRequested));
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
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
