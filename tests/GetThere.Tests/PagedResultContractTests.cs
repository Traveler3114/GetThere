using System.Text.Json;

using GetThereShared.Common;

namespace GetThere.Tests;

/// <summary>
/// Regression origin: a rewrite of <see cref="PagedResult{T}"/> added a second public constructor
/// without <c>[JsonConstructor]</c>. System.Text.Json throws on a type with multiple public
/// parameterized constructors and no marker, which broke the MAUI ticket list at runtime — and a
/// later edit deleted <c>HasNextPage</c>/<c>HasPreviousPage</c>, which the admin pages read over the
/// wire to enable their paging buttons.
/// </summary>
public class PagedResultContractTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Round_trips_through_System_Text_Json()
    {
        var original = new PagedResult<string>(["a", "b"], total: 7, page: 2, perPage: 2);

        var json = JsonSerializer.Serialize(original, CamelCase);
        var restored = JsonSerializer.Deserialize<PagedResult<string>>(json, CamelCase);

        Assert.NotNull(restored);
        Assert.Equal(["a", "b"], restored.Data);
        Assert.Equal(7, restored.Total);
        Assert.Equal(2, restored.Page);
        Assert.Equal(2, restored.PerPage);
        Assert.Equal(4, restored.TotalPages);
    }

    [Fact]
    public void Exposes_the_paging_flags_the_admin_pages_read()
    {
        var json = JsonSerializer.Serialize(new PagedResult<string>(["a"], total: 10, page: 2, perPage: 5), CamelCase);

        // The admin pages branch on these; if they vanish from the payload, `!undefined` disables
        // both buttons and paging silently dies.
        Assert.Contains("hasNextPage", json);
        Assert.Contains("hasPreviousPage", json);
    }

    [Theory]
    [InlineData(0, 20, 1, false, false)]   // empty result set
    [InlineData(10, 20, 1, false, false)]  // single page
    [InlineData(41, 20, 1, true, false)]   // first of three
    [InlineData(41, 20, 2, true, true)]    // middle
    [InlineData(41, 20, 3, false, true)]   // last
    public void Computes_page_boundaries(int total, int perPage, int page, bool hasNext, bool hasPrevious)
    {
        var result = new PagedResult<string>([], total, page, perPage);

        Assert.Equal(hasNext, result.HasNextPage);
        Assert.Equal(hasPrevious, result.HasPreviousPage);
    }

    [Fact]
    public void Does_not_divide_by_zero_when_perPage_is_not_positive()
    {
        var result = new PagedResult<string>([], total: 10, page: 1, perPage: 0);

        Assert.Equal(1, result.TotalPages);
    }
}
