using Microsoft.EntityFrameworkCore;

namespace OpenTripPlannerAPI.Data;

public sealed class OtpReadDbContext : DbContext
{
    public OtpReadDbContext(DbContextOptions<OtpReadDbContext> options) : base(options) { }

    public DbSet<TransitOperatorReadModel> TransitOperators => Set<TransitOperatorReadModel>();
    public DbSet<CountryReadModel> Countries => Set<CountryReadModel>();

    public override int SaveChanges()
        => throw new NotSupportedException("OpenTripPlannerAPI database context is read-only.");

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => throw new NotSupportedException("OpenTripPlannerAPI database context is read-only.");

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("OpenTripPlannerAPI database context is read-only.");

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("OpenTripPlannerAPI database context is read-only.");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransitOperatorReadModel>(entity =>
        {
            entity.ToTable("TransitOperators");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.GtfsFeedUrl).IsRequired(false);
            entity.Property(x => x.GtfsRealtimeFeedUrl).IsRequired(false);
            entity.HasOne(x => x.Country)
                .WithMany(x => x.TransitOperators)
                .HasForeignKey(x => x.CountryId);
        });

        modelBuilder.Entity<CountryReadModel>(entity =>
        {
            entity.ToTable("Countries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired();
        });
    }
}

public sealed class TransitOperatorReadModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CountryId { get; set; }
    public string? GtfsFeedUrl { get; set; }
    public string? GtfsRealtimeFeedUrl { get; set; }
    public CountryReadModel? Country { get; set; }
}

public sealed class CountryReadModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<TransitOperatorReadModel> TransitOperators { get; set; } = [];
}
