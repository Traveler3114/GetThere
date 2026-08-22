using GetThereAPI.Data;

using Microsoft.EntityFrameworkCore;

namespace GetThere.Tests.Search;

/// <summary>
/// Real SQL Server database for search-filter tests. The <c>search</c> parameter on
/// <c>ImportedTicketManager</c> and <c>JourneyManager</c> is translated to SQL (<c>LIKE '%term%'</c>)
/// and relies on the database collation for case-insensitivity. The in-memory provider evaluates
/// <c>Where</c> client-side, so a query that uses <c>ToLowerInvariant()</c> or any other
/// non-translatable call would pass there and fail at runtime on SQL Server — exactly the bug
/// this fixture exists to catch. Every test in this collection therefore runs against the same
/// SQL Server that CI uses (via <c>TESTS_SQL_CONNECTION</c> or a local default).
/// </summary>
public sealed class SearchFixture : IDisposable
{
    public static readonly string ConnectionString = TestDatabase.ConnectionStringFor("GetThereSearchTests");

    public SearchFixture()
    {
        using var db = CreateContext();
        db.Database.EnsureDeleted();
        db.Database.Migrate();
    }

    public static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    public void Dispose()
    {
        using var db = CreateContext();
        db.Database.EnsureDeleted();
    }
}

[CollectionDefinition(Name)]
public class SearchCollection : ICollectionFixture<SearchFixture>
{
    public const string Name = "search";
}
