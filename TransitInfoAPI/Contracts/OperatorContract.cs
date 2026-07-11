using System.ComponentModel.DataAnnotations;

namespace TransitInfoAPI.Contracts;

public class OperatorResponse
{
    public int Id { get; set; }
    public string GlobalId { get; set; } = string.Empty;
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string? Website { get; set; }
}

/// <summary>Minimal operator info used in feed listings.</summary>
public class OperatorBriefResponse
{
    public string GlobalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
}

public record CreateOperatorRequest
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [StringLength(100)] public string ShortName { get; set; } = string.Empty;
    [Url] public string? Website { get; set; }
    [StringLength(50)] public string? GlobalId { get; set; }
}

public record UpdateOperatorRequest
{
    [StringLength(200), MinLength(1)] public string? Name { get; set; }
    [StringLength(100)] public string? ShortName { get; set; }
    [Url] public string? Website { get; set; }
}
