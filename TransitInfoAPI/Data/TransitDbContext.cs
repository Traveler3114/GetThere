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
    public DbSet<CustomFeed> CustomFeeds { get; set; }
    public DbSet<CustomFeedFieldMapping> CustomFeedFieldMappings { get; set; }
    public DbSet<CustomFeedRun> CustomFeedRuns { get; set; }
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
            .HasIndex(fv => fv.Sha1)
            .IsUnique();
        modelBuilder.Entity<FeedVersion>()
            .HasIndex(fv => new { fv.FeedId, fv.IsActive })
            .IsUnique()
            .HasFilter("[IsActive] = 1");

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
            .HasOne(ml => ml.Source)
            .WithMany()
            .HasForeignKey(ml => ml.SourceStationId)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<StationMergeLog>()
            .HasOne(ml => ml.Target)
            .WithMany()
            .HasForeignKey(ml => ml.TargetStationId)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<StationMergeLog>()
            .HasIndex(ml => ml.SourceStationId);
        modelBuilder.Entity<StationMergeLog>()
            .HasIndex(ml => ml.TargetStationId);
        modelBuilder.Entity<StationMergeMovedRawStop>()
            .HasIndex(mrs => mrs.StationMergeLogId);
        modelBuilder.Entity<StationMergeMovedRawStop>()
            .HasOne(mrs => mrs.StationMergeLog)
            .WithMany(ml => ml.MovedRawStops)
            .HasForeignKey(mrs => mrs.StationMergeLogId)
            .OnDelete(DeleteBehavior.Cascade);

        // Trip
        modelBuilder.Entity<Trip>()
            .HasIndex(t => new { t.FeedVersionId, t.TripId })
            .IsUnique();
        modelBuilder.Entity<Trip>()
            .HasIndex(t => t.CanonicalRouteId);

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

        // Missing FK indexes
        modelBuilder.Entity<CanonicalStationOperator>()
            .HasIndex(cso => cso.OperatorId);
        modelBuilder.Entity<CanonicalRoute>()
            .HasIndex(cr => cr.OperatorId);
        modelBuilder.Entity<Feed>()
            .HasIndex(f => f.FeedId)
            .IsUnique();
        modelBuilder.Entity<Alert>()
            .HasIndex(a => a.FeedId);
        modelBuilder.Entity<ReconciliationCandidate>()
            .HasIndex(rc => rc.SuggestedCanonicalStationId);
        modelBuilder.Entity<MobilityStation>()
            .HasIndex(ms => ms.MobilityProviderId);
        modelBuilder.Entity<City>()
            .HasIndex(c => c.CountryId);

        // Custom Feed tables
        modelBuilder.Entity<CustomFeed>(entity =>
        {
            entity.HasIndex(e => e.OperatorId);
            entity.HasIndex(e => e.MobilityProviderId);
            entity.HasOne(e => e.MobilityProvider)
                .WithMany()
                .HasForeignKey(e => e.MobilityProviderId)
                .OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<CustomFeedFieldMapping>(entity =>
        {
            entity.HasIndex(e => e.CustomFeedId);
            entity.HasOne(e => e.CustomFeed)
                .WithMany(e => e.FieldMappings)
                .HasForeignKey(e => e.CustomFeedId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CustomFeedRun>(entity =>
        {
            entity.HasIndex(e => e.CustomFeedId);
            entity.Property(e => e.LogText).HasColumnType("nvarchar(max)");
            entity.HasOne(e => e.CustomFeed)
                .WithMany(e => e.Runs)
                .HasForeignKey(e => e.CustomFeedId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
