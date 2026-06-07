namespace GetThereAPI.Entities;
    public class Country
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // Navigation: All transit operators for this country
        public ICollection<TransitOperator> TransitOperators { get; set; } = [];

        // Navigation: all cities in this country
        public ICollection<City> Cities { get; set; } = [];

        // Navigation: mobility providers operating in this country
        public ICollection<MobilityProvider> MobilityProviders { get; set; } = [];
    }