namespace GetThereShared.Common;

public record PagedResult<T>(List<T> Data, int Total)
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int TotalPages => (int)Math.Ceiling((double)Total / Math.Max(PageSize, 1));
    public bool HasNextPage => Page * PageSize < Total;
    public bool HasPreviousPage => Page > 1;
}
