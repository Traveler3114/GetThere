using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Data;

public class TransitDbContext : DbContext
{
    public TransitDbContext(DbContextOptions<TransitDbContext> options) : base(options) { }

    public DbSet<Country> Countries { get; set; }
    public DbSet<Region> Regions { get; set; }
    public DbSet<City> Cities { get; set; }
    public DbSet<Operator> Operators { get; set; }
    public DbSet<Feed> Feeds { get; set; }
    public DbSet<FeedConverter> FeedConverters { get; set; }
    public DbSet<CanonicalStation> CanonicalStations { get; set; }
    public DbSet<CanonicalStationOperator> CanonicalStationOperators { get; set; }
    public DbSet<CanonicalRoute> CanonicalRoutes { get; set; }
    public DbSet<ReconciliationCandidate> ReconciliationCandidates { get; set; }
    public DbSet<MobilityProvider> MobilityProviders { get; set; }
    public DbSet<MobilityStation> MobilityStations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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

            // Disable cascade deletes globally — SQL Server doesn't allow multiple cascade paths
            foreach (var fk in entityType.GetForeignKeys())
                fk.DeleteBehavior = DeleteBehavior.Restrict;
        }

        // CanonicalStationOperator composite key
        modelBuilder.Entity<CanonicalStationOperator>()
            .HasKey(cso => new { cso.CanonicalStationId, cso.OperatorId });

        // Decimal precision for ReconciliationCandidate
        modelBuilder.Entity<ReconciliationCandidate>(entity =>
        {
            entity.Property(e => e.ConfidenceScore).HasPrecision(5, 4);
            entity.Property(e => e.DistanceMeters).HasPrecision(10, 4);
            entity.Property(e => e.NameSimilarityScore).HasPrecision(5, 4);
        });

        // GlobalId unique indexes
        modelBuilder.Entity<Operator>()
            .HasIndex(o => o.GlobalId)
            .IsUnique();

        modelBuilder.Entity<CanonicalStation>()
            .HasIndex(cs => cs.GlobalId)
            .IsUnique();

        modelBuilder.Entity<CanonicalRoute>()
            .HasIndex(cr => cr.GlobalId)
            .IsUnique();
    }
}
