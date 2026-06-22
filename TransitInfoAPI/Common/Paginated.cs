namespace TransitInfoAPI.Common;

public record Paginated<T>(List<T> Data, int Total, int Page, int PerPage, int TotalPages)
{
    public Paginated(List<T> data, int total, int page, int perPage)
        : this(data, total, page, perPage, perPage > 0 ? (int)Math.Ceiling((double)total / perPage) : 0) { }
}
