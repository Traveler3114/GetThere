using TransitInfoAPI.Enums;

namespace TransitInfoAPI.Entities;

public class CustomFeedTableFieldMapping
{
    public int Id { get; set; }
    public int CustomFeedTableConfigId { get; set; }
    public CustomFeedTableConfig CustomFeedTableConfig { get; set; } = null!;
    public int SortOrder { get; set; }
    public string SourceExpression { get; set; } = string.Empty;
    public string TargetField { get; set; } = string.Empty;
    public MappingKind MappingKind { get; set; }
}
