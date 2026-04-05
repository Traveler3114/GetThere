namespace GetThereShared.Dtos;

/// <summary>
/// A country entry used for the country selector.
/// Returned by GET /countries.
/// </summary>
public class CountryDto
{
    public int    Id   { get; set; }
    public string Name { get; set; } = string.Empty;
}
