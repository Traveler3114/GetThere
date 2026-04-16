using GetThereAPI.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GetThereAPI.Data
{
    public class AppDbContext : IdentityDbContext<AppUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Wallet> Wallets { get; set; }
        public DbSet<WalletTransaction> WalletTransactions { get; set; }
        public DbSet<Ticket> Tickets { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<TransitOperator> TransitOperators { get; set; }
        public DbSet<TransportType> TransportTypes { get; set; }
        public DbSet<PaymentProvider> PaymentProviders { get; set; }
        public DbSet<Country> Countries { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<MobilityProvider> MobilityProviders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Automatically store all enum properties as readable strings in the DB
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    var type = property.ClrType;
                    var underlying = Nullable.GetUnderlyingType(type) ?? type;

                    if (underlying.IsEnum)
                    {
                        var converterType = typeof(EnumToStringConverter<>).MakeGenericType(underlying);
                        var converter = (ValueConverter)Activator.CreateInstance(converterType)!;
                        property.SetValueConverter(converter);
                    }
                }
            }


            //INITIAL DATA SEEDING, REMOVE BEFORE PRODUCTION DEPLOYMENT


            modelBuilder.Entity<Country>().HasData(
                new Country { Id = 1, Name = "Croatia" },
                new Country { Id = 2, Name = "Slovenia" },
                new Country { Id = 3, Name = "Austria" },
                new Country { Id = 4, Name = "Germany" },
                new Country { Id = 5, Name = "France" },
                new Country { Id = 6, Name = "Italy" },
                new Country { Id = 7, Name = "Poland" },
                new Country { Id = 8, Name = "Czechia" },
                new Country { Id = 9, Name = "Hungary" },
                new Country { Id = 10, Name = "Switzerland" },
                new Country { Id = 11, Name = "Slovakia" },
                new Country { Id = 12, Name = "Spain" }
            );

            modelBuilder.Entity<City>().HasData(
                new City { Id = 1, Name = "Zagreb", CountryId = 1 },
                new City { Id = 2, Name = "Ljubljana", CountryId = 2 },
                new City { Id = 3, Name = "Vienna", CountryId = 3 },
                new City { Id = 4, Name = "Berlin", CountryId = 4 },
                new City { Id = 5, Name = "Paris", CountryId = 5 },
                new City { Id = 6, Name = "Rome", CountryId = 6 },
                new City { Id = 7, Name = "Warsaw", CountryId = 7 },
                new City { Id = 8, Name = "Prague", CountryId = 8 },
                new City { Id = 9, Name = "Budapest", CountryId = 9 },
                new City { Id = 10, Name = "Zurich", CountryId = 10 },
                new City { Id = 11, Name = "Bratislava", CountryId = 11 },
                new City { Id = 12, Name = "Madrid", CountryId = 12 }
            );

            modelBuilder.Entity<TransitOperator>().HasData(
                 new TransitOperator
                 {
                     Id = 1,
                     Name = "ZET",
                     LogoUrl = null,
                     TicketApiBaseUrl = "",
                     TicketApiKey = "",
                     GtfsFeedUrl = "https://zet.hr/gtfs-scheduled/latest",
                     GtfsRealtimeFeedUrl = "https://zet.hr/gtfs-rt-protobuf",
                     CreatedAt = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                     CountryId = 1,
                     CityId = 1
                 },
                 new TransitOperator
                 {
                     Id = 2,
                     Name = "HZPP",
                     LogoUrl = null,
                     TicketApiBaseUrl = "",
                     TicketApiKey = "",
                     GtfsFeedUrl = "https://www.hzpp.hr/GTFS_files.zip",
                     GtfsRealtimeFeedUrl = "http://127.0.0.1:5000/rt/hzpp",
                     CreatedAt = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                     CountryId = 1,
                     CityId = null
                 }
            );

            modelBuilder.Entity<TransportType>().HasData(
                new TransportType { Id = 1, GtfsRouteType = 0, Name = "Tram", IconFile = "tram.png", Color = "#1264AB" },
                new TransportType { Id = 2, GtfsRouteType = 3, Name = "Bus", IconFile = "bus.png", Color = "#126400" },
                new TransportType { Id = 3, GtfsRouteType = 2, Name = "Train", IconFile = "train.png", Color = "#FF6B00" }
            );

            modelBuilder.Entity<PaymentProvider>().HasData(
                new PaymentProvider
                {
                    Id = 1,
                    Name = "MockPay",
                    ApiBaseUrl = "https://mockpay.example.com",
                    ApiKey = "MOCK_API_KEY",
                    WebhookSecret = null,
                    IsActive = true,
                    CreatedAt = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc)
                },
                new PaymentProvider
                {
                    Id = 2,
                    Name = "TestPay",
                    ApiBaseUrl = "https://testpay.example.com",
                    ApiKey = "TEST_API_KEY",
                    WebhookSecret = "SECRET123",
                    IsActive = true,
                    CreatedAt = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc)
                }
            );

            modelBuilder.Entity<MobilityProvider>().HasData(
                new MobilityProvider
                {
                    Id = 1,
                    Name = "Bajs / Nextbike",
                    LogoUrl = null,
                    Type = MobilityType.BIKE_STATION,
                    FeedFormat = MobilityFeedFormat.NEXTBIKE_API,
                    ApiBaseUrl = "https://nextbike.net/maps/nextbike-live.json",
                    ApiKey = null,
                    // No cityUid filter — fetch all Nextbike stations worldwide.
                    // Countries are detected dynamically from the feed; no manual DB links needed.
                    AdapterConfig = null,
                    CreatedAt = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc)
                }
            );

            // MobilityProvider ↔ Country and MobilityProvider ↔ City relationships are defined
            // here so EF Core generates the join tables, but no seed rows are inserted.
            // Country coverage for mobility providers is determined dynamically at runtime
            // from the live feed data (see MobilityManager.HasStationsInCountry).
            modelBuilder.Entity<MobilityProvider>()
                .HasMany(mp => mp.Countries)
                .WithMany(c => c.MobilityProviders)
                .UsingEntity(j => j.ToTable("MobilityProviderCountry"));

            modelBuilder.Entity<MobilityProvider>()
                .HasMany(mp => mp.Cities)
                .WithMany(c => c.MobilityProviders)
                .UsingEntity(j => j.ToTable("MobilityProviderCity"));
        }
    }
}
