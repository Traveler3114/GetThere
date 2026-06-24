namespace TransitInfoAPI.Common;

public record Paginated<T>(List<T> Data, int Total, int Page, int PerPage, int TotalPages)
{
    public Paginated(List<T> data, int total, int page, int perPage)
        : this(data, total, page, perPage < 1 ? 1 : perPage, perPage < 1 ? 1 : (int)Math.Ceiling((double)total / perPage)) { }
}
