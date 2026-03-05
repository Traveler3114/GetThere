namespace GetThereAPI.Entities
{
    public class TransitOperator
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public string ApiBaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Every operator must have a country
        public int CountryId { get; set; }
        public Country Country { get; set; } = null!;

        // Some are city-specific (nullable for e.g. national trains)
        public int? CityId { get; set; }
        public City? City { get; set; }
    }
}