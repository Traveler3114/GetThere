namespace GetThereAPI.Entities;
    public class City
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public int CountryId { get; set; }
        public Country Country { get; set; } = null!;

        // Navigation: All transit operators in this city
        public ICollection<TransitOperator> TransitOperators { get; set; } = [];

        // Navigation: mobility providers operating in this city
        public ICollection<MobilityProvider> MobilityProviders { get; set; } = [];
    }