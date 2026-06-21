using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Data;

public class TransitDbContext : DbContext
{
    public TransitDbContext(DbContextOptions<TransitDbContext> options) : base(options) { }

    public DbSet<Country> Countries { get; set; }
    public DbSet<City> Cities { get; set; }
    public DbSet<Operator> Operators { get; set; }
    public DbSet<Feed> Feeds { get; set; }
    public DbSet<FeedVersion> FeedVersions { get; set; }
    public DbSet<Agency> Agencies { get; set; }
    public DbSet<CanonicalStation> CanonicalStations { get; set; }
    public DbSet<CanonicalStationOperator> CanonicalStationOperators { get; set; }
    public DbSet<CanonicalRoute> CanonicalRoutes { get; set; }
    public DbSet<RawStop> RawStops { get; set; }
    public DbSet<ReconciliationCandidate> ReconciliationCandidates { get; set; }
    public DbSet<MobilityProvider> MobilityProviders { get; set; }
    public DbSet<MobilityStation> MobilityStations { get; set; }
    public DbSet<Alert> Alerts { get; set; }
    public DbSet<Place> Places { get; set; }
    public DbSet<Trip> Trips { get; set; }
    public DbSet<StopTime> StopTimes { get; set; }
    public DbSet<Calendar> Calendars { get; set; }
    public DbSet<CalendarDate> CalendarDates { get; set; }
    public DbSet<Shape> Shapes { get; set; }
    public DbSet<StationSplitLog> StationSplitLogs { get; set; }
    public DbSet<StationMergeLog> StationMergeLogs { get; set; }

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
                    property.SetMaxLength(50);
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
            entity.Property(e => e.DistanceMeters).HasPrecision(14, 4);
            entity.Property(e => e.NameSimilarityScore).HasPrecision(5, 4);
            entity.Property(e => e.AutoMergeNameThresholdAtDecision).HasPrecision(5, 4);
            entity.Property(e => e.AutoMergeDistanceMetersAtDecision).HasPrecision(14, 4);
            entity.Property(e => e.ManualReviewNameThresholdAtDecision).HasPrecision(5, 4);
            entity.Property(e => e.ManualReviewDistanceMetersAtDecision).HasPrecision(14, 4);
        });

        // Country IsoCode unique index
        modelBuilder.Entity<Country>()
            .HasIndex(c => c.IsoCode)
            .IsUnique();

        // OnestopId unique indexes
        modelBuilder.Entity<Operator>()
            .HasIndex(o => o.OnestopId)
            .IsUnique();

        modelBuilder.Entity<CanonicalStation>()
            .HasIndex(cs => cs.OnestopId)
            .IsUnique();

        modelBuilder.Entity<CanonicalRoute>()
            .HasIndex(cr => cr.OnestopId)
            .IsUnique();

        // FeedVersion
        modelBuilder.Entity<FeedVersion>()
            .HasIndex(fv => fv.Sha1);
        modelBuilder.Entity<FeedVersion>()
            .HasIndex(fv => new { fv.FeedId, fv.IsActive });

        // RawStop
        modelBuilder.Entity<RawStop>()
            .HasIndex(rs => new { rs.FeedVersionId, rs.RawStopId })
            .IsUnique();
        modelBuilder.Entity<RawStop>()
            .HasIndex(rs => rs.CanonicalStationId);

        // ReconciliationCandidate
        modelBuilder.Entity<ReconciliationCandidate>()
            .HasIndex(rc => rc.RawStopId);

        // StationSplitLog
        modelBuilder.Entity<StationSplitLog>()
            .HasIndex(sl => sl.CandidateStationId);

        // StationMergeLog
        modelBuilder.Entity<StationMergeLog>()
            .HasIndex(ml => ml.SourceStationId);
        modelBuilder.Entity<StationMergeLog>()
            .HasIndex(ml => ml.TargetStationId);

        // Trip
        modelBuilder.Entity<Trip>()
            .HasIndex(t => new { t.FeedVersionId, t.TripId });

        // StopTime
        modelBuilder.Entity<StopTime>()
            .HasIndex(st => st.TripId);
        modelBuilder.Entity<StopTime>()
            .HasIndex(st => st.CanonicalStationId);
        modelBuilder.Entity<StopTime>()
            .HasIndex(st => st.RawStopEntityId);
        modelBuilder.Entity<StopTime>()
            .HasOne(st => st.RawStopEntity)
            .WithMany()
            .HasForeignKey(st => st.RawStopEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
