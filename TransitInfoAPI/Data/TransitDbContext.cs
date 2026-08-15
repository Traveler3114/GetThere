using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using TransitInfoAPI.Entities;

namespace TransitInfoAPI.Data;

public class TransitDbContext : IdentityDbContext<AppUser>
{
    public TransitDbContext(DbContextOptions<TransitDbContext> options) : base(options) { }

    public DbSet<Country> Countries { get; set; } = null!;
    public DbSet<City> Cities { get; set; } = null!;
    public DbSet<Operator> Operators { get; set; } = null!;
    public DbSet<Feed> Feeds { get; set; } = null!;
    public DbSet<FeedVersion> FeedVersions { get; set; } = null!;
    public DbSet<CustomSource> CustomSources { get; set; } = null!;
    public DbSet<CustomSourceRequest> CustomSourceRequests { get; set; } = null!;
    public DbSet<CustomSourceMapping> CustomSourceMappings { get; set; } = null!;
    public DbSet<CustomSourceRun> CustomSourceRuns { get; set; } = null!;
    public DbSet<Agency> Agencies { get; set; } = null!;
    public DbSet<CanonicalStation> CanonicalStations { get; set; } = null!;
    public DbSet<CanonicalStationOperator> CanonicalStationOperators { get; set; } = null!;
    public DbSet<CanonicalRoute> CanonicalRoutes { get; set; } = null!;
    public DbSet<RawStop> RawStops { get; set; } = null!;
    public DbSet<ReconciliationCandidate> ReconciliationCandidates { get; set; } = null!;
    public DbSet<MobilityStation> MobilityStations { get; set; } = null!;
    public DbSet<Alert> Alerts { get; set; } = null!;
    public DbSet<Place> Places { get; set; } = null!;
    public DbSet<Trip> Trips { get; set; } = null!;
    public DbSet<StopTime> StopTimes { get; set; } = null!;
    public DbSet<Calendar> Calendars { get; set; } = null!;
    public DbSet<CalendarDate> CalendarDates { get; set; } = null!;
    public DbSet<Shape> Shapes { get; set; } = null!;
    public DbSet<StationSplitLog> StationSplitLogs { get; set; } = null!;
    public DbSet<StationMergeLog> StationMergeLogs { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Identity table names
        modelBuilder.Entity<AppUser>(b => b.ToTable("AspNetUsers"));
        modelBuilder.Entity<IdentityRole>(b => b.ToTable("AspNetRoles"));
        modelBuilder.Entity<IdentityUserRole<string>>(b => b.ToTable("AspNetUserRoles"));
        modelBuilder.Entity<IdentityUserClaim<string>>(b => b.ToTable("AspNetUserClaims"));
        modelBuilder.Entity<IdentityUserLogin<string>>(b => b.ToTable("AspNetUserLogins"));
        modelBuilder.Entity<IdentityRoleClaim<string>>(b => b.ToTable("AspNetRoleClaims"));
        modelBuilder.Entity<IdentityUserToken<string>>(b => b.ToTable("AspNetUserTokens"));

        // ── Conventions first, per-entity overrides after ──────────────────────────────────────
        //
        // This loop used to sit *below* the RefreshToken and AuditLog blocks, which meant it
        // overwrote them: RefreshToken declared OnDelete(Cascade) and silently got Restrict, so
        // deleting a user was blocked by their tokens rather than cascading. GetThereAPI's
        // AppDbContext has always run these in this order, which is why its overrides survive.
        //
        // Anything configured after this point wins, which is the intent.
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

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens");
            b.HasKey(rt => rt.Id);
            b.HasIndex(rt => rt.Token).IsUnique();
            // Now actually takes effect: a user's tokens go with the user rather than blocking the
            // delete. This is the single deliberate cascade in the model.
            b.HasOne(rt => rt.User).WithMany().HasForeignKey(rt => rt.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(al => al.CreatedAt);
            entity.HasOne(al => al.User)
                  .WithMany()
                  .HasForeignKey(al => al.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

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

        // ── Lengths on indexed string columns ─────────────────────────────────────────────────
        //
        // Every string column indexed below had no configured length, so EF widened each to
        // nvarchar(450) — the largest declaration that still fits an index key. That is why these
        // indexes exist at all rather than failing to create: SQL Server refuses to index
        // nvarchar(max), which is the other half of the bug docs/database-drift.md records for
        // RefreshTokens.Token.
        //
        // What 450 is NOT is a per-row cost. This block used to argue from GetThereAPI's
        // Purchase.Status comment — "~900 bytes per row in an index over four short words" — and
        // that is wrong: nvarchar stores the length of the value, not the declared maximum.
        // Narrowing these columns freed no storage, measured on 500,000 seeded StopTimes with a
        // rebuild-only control to separate the width change from the defragmentation that
        // ALTER COLUMN causes. The numbers are in docs/changelog.md, 2026-08-15.
        //
        // The sizes are still worth declaring, for reasons that are not storage: a malformed feed
        // can no longer write a 450-character id, and a key of int + 2 × nvarchar(450) is 1804
        // bytes, past the 1700-byte limit — SQL Server creates such an index anyway and fails later
        // on an insert, so the headroom is what keeps a future composite index honest.
        //
        // Applied in SizeIndexedStringColumns. Each length now sits beside the index that motivates
        // it, so the two cannot drift apart again:
        //
        //   Country.IsoCode            8     Operator.OnestopId          128
        //   FeedVersion.Sha1          64     CanonicalStation.OnestopId  128
        //   RawStop.RawStopId        128     CanonicalRoute.OnestopId    128
        //   StopTime.RawStopId       128     Feed.FeedId                 128
        //   Trip.TripId              128     Feed.OnestopId              128 (was nvarchar(max),
        //                                    the one OnestopId with no index)
        //
        // The lengths sat here as a comment until an SDK was available, because a model change
        // without its migration is not inert: EF Core raises PendingModelChangesWarning as an error
        // inside Database.Migrate(), which every database-backed fixture calls, so adding these
        // alone turns the suite red. They landed in the same commit as `dotnet ef migrations add`.
        //
        // Sizes are generous against real data rather than minimal, because a length below what a
        // live row already holds fails the migration: GTFS ids and Onestop slugs run to tens of
        // characters, not hundreds, and Sha1 is exactly 40 hex digits (64 leaves room should
        // ExternalFeedSource.ComputeHash ever move off SHA-1, which it should). Query the maxima
        // before applying this to any database that already holds a feed.

        // Country IsoCode unique index
        modelBuilder.Entity<Country>()
            .Property(c => c.IsoCode)
            .HasMaxLength(8);
        modelBuilder.Entity<Country>()
            .HasIndex(c => c.IsoCode)
            .IsUnique();

        // OnestopId unique indexes
        modelBuilder.Entity<Operator>()
            .Property(o => o.OnestopId)
            .HasMaxLength(128);
        modelBuilder.Entity<Operator>()
            .HasIndex(o => o.OnestopId)
            .IsUnique();

        modelBuilder.Entity<CanonicalStation>()
            .Property(cs => cs.OnestopId)
            .HasMaxLength(128);
        modelBuilder.Entity<CanonicalStation>()
            .HasIndex(cs => cs.OnestopId)
            .IsUnique();

        modelBuilder.Entity<CanonicalRoute>()
            .Property(cr => cr.OnestopId)
            .HasMaxLength(128);
        modelBuilder.Entity<CanonicalRoute>()
            .HasIndex(cr => cr.OnestopId)
            .IsUnique();

        // FeedVersion
        modelBuilder.Entity<FeedVersion>()
            .Property(fv => fv.Sha1)
            .HasMaxLength(64);
        modelBuilder.Entity<FeedVersion>()
            .HasIndex(fv => fv.Sha1)
            .IsUnique();
        modelBuilder.Entity<FeedVersion>()
            .HasIndex(fv => new { fv.FeedId, fv.IsActive })
            .IsUnique()
            .HasFilter("[IsActive] = 1");

        // RawStop
        modelBuilder.Entity<RawStop>()
            .Property(rs => rs.RawStopId)
            .HasMaxLength(128);
        modelBuilder.Entity<RawStop>()
            .HasIndex(rs => new { rs.FeedVersionId, rs.RawStopId })
            .IsUnique();
        modelBuilder.Entity<RawStop>()
            .HasIndex(rs => rs.CanonicalStationId);

        // StopTime
        modelBuilder.Entity<StopTime>()
            .Property(st => st.RawStopId)
            .HasMaxLength(128);
        modelBuilder.Entity<StopTime>()
            .HasIndex(st => st.RawStopId);

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
            .Property(t => t.TripId)
            .HasMaxLength(128);
        modelBuilder.Entity<Trip>()
            .HasIndex(t => new { t.FeedVersionId, t.TripId })
            .IsUnique();
        modelBuilder.Entity<Trip>()
            .HasIndex(t => t.CanonicalRouteId);

        // StopTime
        modelBuilder.Entity<StopTime>()
            .HasIndex(st => st.TripId);
        modelBuilder.Entity<StopTime>()
            .HasIndex(st => new { st.CanonicalStationId, st.DepartureTime })
            .IncludeProperties(st => st.TripId);
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
            .Property(f => f.FeedId)
            .HasMaxLength(128);
        // Feed.OnestopId is the one OnestopId with no index; it was nvarchar(max) rather than
        // nvarchar(450) for exactly that reason, and is bounded here for consistency with the other
        // three, not to make an index possible.
        modelBuilder.Entity<Feed>()
            .Property(f => f.OnestopId)
            .HasMaxLength(128);
        modelBuilder.Entity<Feed>()
            .HasIndex(f => f.FeedId)
            .IsUnique();

        // ── Custom sources ────────────────────────────────────────────────────────────────────
        // Deletes stay Restrict here as everywhere else, so removing a source with runs or requests
        // is an explicit, ordered operation in the manager rather than a silent cascade.
        modelBuilder.Entity<CustomSource>(entity =>
        {
            entity.Property(cs => cs.Name).HasMaxLength(200);
            entity.Property(cs => cs.ExtractorKey).HasMaxLength(100);
            entity.HasIndex(cs => cs.OperatorId);
            entity.HasIndex(cs => cs.IsActive);
        });

        modelBuilder.Entity<CustomSourceRequest>(entity =>
        {
            entity.Property(r => r.HttpMethod).HasMaxLength(10);
            entity.Property(r => r.DistinctBy).HasMaxLength(100);
            entity.HasIndex(r => new { r.CustomSourceId, r.SortOrder });
        });

        modelBuilder.Entity<CustomSourceMapping>(entity =>
        {
            entity.Property(m => m.SourceExpression).HasMaxLength(400);
            entity.Property(m => m.TargetField).HasMaxLength(100);
            entity.HasIndex(m => new { m.CustomSourceRequestId, m.SortOrder });
        });

        modelBuilder.Entity<CustomSourceRun>(entity =>
        {
            entity.HasIndex(r => new { r.CustomSourceId, r.StartedAt });
        });

        modelBuilder.Entity<Alert>()
            .HasIndex(a => a.FeedId);
        modelBuilder.Entity<ReconciliationCandidate>()
            .HasIndex(rc => rc.SuggestedCanonicalStationId);
        modelBuilder.Entity<MobilityStation>()
            .HasIndex(ms => ms.OperatorId);

        // A station id is unique within an operator — that is the key the GBFS upsert matches on.
        // Without this nothing stopped duplicates existing, and the upsert's ToDictionary threw on
        // them, taking down the poll for that operator.
        //
        // The length is explicit because the column was nvarchar(max), which cannot be indexed at
        // all; left implicit EF widens it to 450, far more than a GBFS station_id needs. (That
        // widening costs no storage — see the correction in the lengths block above — but it does
        // spend index-key budget and accept ids no feed should be sending.)
        modelBuilder.Entity<MobilityStation>()
            .Property(ms => ms.StationId)
            .HasMaxLength(128);

        modelBuilder.Entity<MobilityStation>()
            .HasIndex(ms => new { ms.OperatorId, ms.StationId })
            .IsUnique();
        modelBuilder.Entity<City>()
            .HasIndex(c => c.CountryId);
    }
}
