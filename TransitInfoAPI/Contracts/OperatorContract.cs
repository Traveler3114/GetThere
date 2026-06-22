namespace TransitInfoAPI.Contracts;

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

public class OperatorBriefResponse
{
    public string GlobalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
}

public class CreateOperatorRequest
{
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public int CountryId { get; set; }
    public string? Website { get; set; }
    public string? GlobalId { get; set; }
}

public class UpdateOperatorRequest
{
    public string? Name { get; set; }
    public string? ShortName { get; set; }
    public int? CountryId { get; set; }
    public string? Website { get; set; }
}
