using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class CustomFeedFieldMapping
{
    public int Id { get; set; }
    public int CustomFeedId { get; set; }
    public CustomFeed CustomFeed { get; set; } = null!;
    public int SortOrder { get; set; }
    public string SourceExpression { get; set; } = string.Empty;
    public string TargetField { get; set; } = string.Empty;
    public MappingKind MappingKind { get; set; }
}
