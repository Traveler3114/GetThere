using GetThereShared.Enums;

namespace GetThereAPI.Models;

public class RequiredInput
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsSensitive { get; set; }
    public bool IsRequired { get; set; }
}
