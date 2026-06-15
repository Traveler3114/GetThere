namespace TransitInfoAPI.Models;

public class PaginationMeta
{
    public int After { get; set; }
    public string? Next { get; set; }
    public int TotalCount { get; set; }
}
