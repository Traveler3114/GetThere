using TransitInfoAPI.Entities;
using TransitInfoAPI.Managers;

namespace GetThere.Tests;

/// <summary>
/// Regression origin: the candidate lookup read only the grid cell containing the raw stop, so two
/// stations metres apart but on opposite sides of a ~22 km cell boundary were never compared. The
/// failure was silent — no error, just duplicate CanonicalStation rows that reconciliation should
/// have merged.
/// </summary>
public class ReconciliationGridTests
{
    private const double CellSize = 0.2;

    private static Dictionary<string, List<CanonicalStation>> GridOf(params CanonicalStation[] stations)
    {
        Dictionary<string, List<CanonicalStation>> grid = [];
        foreach (var station in stations)
        {
            var key = ReconciliationManager.GetSpatialCellKey(station.Latitude, station.Longitude);
            if (!grid.TryGetValue(key, out var bucket))
                grid[key] = bucket = [];
            bucket.Add(station);
        }
        return grid;
    }

    private static CanonicalStation StationAt(int id, double lat, double lon) =>
        new() { Id = id, Name = $"Station {id}", Latitude = lat, Longitude = lon };

    [Fact]
    public void Finds_a_station_on_the_other_side_of_a_cell_boundary()
    {
        // 45.8 is exactly a cell edge (45.8 / 0.2 = 229). These two points are ~22 m apart but
        // Math.Floor puts them in different cells.
        var justBelow = 45.7999;
        var justAbove = 45.8001;

        var neighbour = StationAt(1, justAbove, 16.0);
        var grid = GridOf(neighbour);

        Assert.NotEqual(
            ReconciliationManager.GetSpatialCellKey(justBelow, 16.0),
            ReconciliationManager.GetSpatialCellKey(justAbove, 16.0));

        var found = ReconciliationManager.GetStationsNearCell(grid, justBelow, 16.0);

        Assert.Contains(neighbour, found);
    }

    [Fact]
    public void Finds_a_station_across_a_longitude_boundary()
    {
        var neighbour = StationAt(2, 45.5, 16.0001);
        var grid = GridOf(neighbour);

        var found = ReconciliationManager.GetStationsNearCell(grid, 45.5, 15.9999);

        Assert.Contains(neighbour, found);
    }

    [Fact]
    public void Finds_a_station_across_a_diagonal_corner()
    {
        var neighbour = StationAt(3, 45.8001, 16.0001);
        var grid = GridOf(neighbour);

        var found = ReconciliationManager.GetStationsNearCell(grid, 45.7999, 15.9999);

        Assert.Contains(neighbour, found);
    }

    [Fact]
    public void Returns_stations_in_the_containing_cell()
    {
        var same = StationAt(4, 45.71, 16.11);
        var grid = GridOf(same);

        Assert.Contains(same, ReconciliationManager.GetStationsNearCell(grid, 45.72, 16.12));
    }

    [Fact]
    public void Excludes_stations_more_than_one_cell_away()
    {
        var faraway = StationAt(5, 45.5 + (3 * CellSize), 16.0);
        var grid = GridOf(faraway);

        Assert.DoesNotContain(faraway, ReconciliationManager.GetStationsNearCell(grid, 45.5, 16.0));
    }

    [Fact]
    public void Returns_empty_for_an_empty_grid()
    {
        Assert.Empty(ReconciliationManager.GetStationsNearCell([], 45.5, 16.0));
    }

    [Fact]
    public void Cell_keys_are_stable_across_cultures()
    {
        // Grid keys are built and looked up in different call sites; a culture-dependent key format
        // would make them disagree under a comma-decimal locale.
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("hr-HR");
            var croatian = ReconciliationManager.GetSpatialCellKey(45.81, 15.98);

            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            var invariant = ReconciliationManager.GetSpatialCellKey(45.81, 15.98);

            Assert.Equal(invariant, croatian);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
