using GetThereAPI.Entities;
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
                new Country { Id = 1, Name = "Croatia" }
            );

            modelBuilder.Entity<City>().HasData(
                new City { Id = 1, Name = "Zagreb", CountryId = 1 }
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
                    RealtimeFeedFormat = "GTFS_RT_PROTO",
                    RealtimeAuthType = "NONE",
                    RealtimeAuthConfig = null,
                    RealtimeAdapterConfig = null,
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
                    GtfsRealtimeFeedUrl = null,
                    RealtimeFeedFormat = "NONE",
                    RealtimeAuthType = "NONE",
                    RealtimeAuthConfig = null,
                    RealtimeAdapterConfig = null,
                    CreatedAt = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                    CountryId = 1,
                    CityId = null
                }
            );

            modelBuilder.Entity<TransportType>().HasData(
                new TransportType { Id = 1, GtfsRouteType = 0, Name = "Tram", IconFile = "tram.png", Color = "#1264AB" },
                new TransportType { Id = 2, GtfsRouteType = 3, Name = "Bus", IconFile = "bus.png", Color = "#126400" },
                new TransportType { Id = 3, GtfsRouteType = 2, Name = "Train", IconFile = "train.png", Color = "#6a1b9a" }
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
        }
    }
}