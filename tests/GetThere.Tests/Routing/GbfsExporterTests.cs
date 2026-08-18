using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using TransitInfoAPI.Data;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Routing;
using TransitInfoAPI.Routing.Export;

namespace GetThere.Tests.Routing;

/// <summary>
/// The GBFS projection is what turns bike share into a routing mode. These pin that it is spatially
/// scoped like the GTFS export and that live dock counts survive the projection into station_status.
/// </summary>
public class GbfsExporterTests
{
    private static TransitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TransitDbContext>()
            .UseInMemoryDatabase($"gbfs-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Fact]
    public async Task Projects_in_scope_docks_with_live_counts_and_excludes_out_of_scope()
    {
        using var db = NewContext();
        db.MobilityStations.AddRange(
            new MobilityStation { Id = 1, StationId = "nb-1", Name = "Jelačić", Latitude = 45.813, Longitude = 15.977, Capacity = 20, AvailableVehicles = 7, LastUpdated = DateTime.UtcNow },
            new MobilityStation { Id = 2, StationId = "nb-far", Name = "Split", Latitude = 43.51, Longitude = 16.44, Capacity = 10, AvailableVehicles = 3, LastUpdated = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var options = Options.Create(new RoutingOptions
        {
            Scope = new BoundingBox { MinLat = 45.6, MinLon = 15.7, MaxLat = 45.95, MaxLon = 16.25 },
        });

        var result = await new GbfsExporter(db, options).ExportAsync();

        Assert.Equal(1, result.StationCount); // the Split dock is outside the Zagreb scope

        var status = JsonDocument.Parse(result.StationStatus).RootElement
            .GetProperty("data").GetProperty("stations");
        var dock = status.EnumerateArray().Single();
        Assert.Equal("nb-1", dock.GetProperty("station_id").GetString());
        Assert.Equal(7, dock.GetProperty("num_bikes_available").GetInt32());
        Assert.Equal(13, dock.GetProperty("num_docks_available").GetInt32()); // 20 capacity - 7 bikes
    }
}
