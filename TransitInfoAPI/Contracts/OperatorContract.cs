using System.ComponentModel.DataAnnotations;

namespace TransitInfoAPI.Contracts;

/// <summary>Full operator details including country association.</summary>
public class OperatorResponse
{
    public int Id { get; set; }
    public string GlobalId { get; set; } = string.Empty;
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? CountryName { get; set; }
}

/// <summary>Minimal operator info used in feed listings.</summary>
public class OperatorBriefResponse
{
    public string GlobalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
}

/// <summary>Request body for creating an operator.</summary>
public class CreateOperatorRequest
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [StringLength(100)] public string ShortName { get; set; } = string.Empty;
    [Range(1, int.MaxValue)] public int CountryId { get; set; }
    [Url] public string? Website { get; set; }
    [StringLength(50)] public string? GlobalId { get; set; }
}

public class UpdateOperatorRequest
{
    [StringLength(200)] public string? Name { get; set; }
    [StringLength(100)] public string? ShortName { get; set; }
    [Range(1, int.MaxValue)] public int? CountryId { get; set; }
    [Url] public string? Website { get; set; }
}
