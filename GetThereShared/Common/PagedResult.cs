using System.Text.Json.Serialization;

namespace GetThereShared.Common;

public record PagedResult<T>
{
    public List<T> Data { get; init; }
    public int Total { get; init; }
    public int Page { get; init; }
    public int PerPage { get; init; }
    public int TotalPages { get; init; }

    [JsonConstructor]
    public PagedResult(List<T> data, int total, int page, int perPage, int totalPages)
    {
        Data = data;
        Total = total;
        Page = page;
        PerPage = perPage;
        TotalPages = totalPages;
    }

    public PagedResult(List<T> data, int total, int page, int perPage)
        : this(data, total, page, perPage, perPage < 1 ? 1 : (int)Math.Ceiling((double)total / perPage)) { }

    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
