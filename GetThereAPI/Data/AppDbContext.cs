using GetThereAPI.Entities;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GetThereAPI.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<Wallet> Wallets { get; set; } = null!;
    public DbSet<WalletTransaction> WalletTransactions { get; set; } = null!;
    public DbSet<UserSettings> UserSettings { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<TicketingAdapter> TicketingAdapters { get; set; } = null!;
    public DbSet<TicketOption> TicketOptions { get; set; } = null!;
    public DbSet<Purchase> Purchases { get; set; } = null!;
    public DbSet<Ticket> Tickets { get; set; } = null!;
    public DbSet<ImportedTicket> ImportedTickets { get; set; } = null!;
    public DbSet<TicketUpload> TicketUploads { get; set; } = null!;
    public DbSet<Journey> Journeys { get; set; } = null!;

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

            foreach (var fk in entityType.GetForeignKeys())
                fk.DeleteBehavior = DeleteBehavior.Restrict;
        }

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            // Token holds a base64 SHA-256 hash (44 chars). It was previously unbounded, which made
            // it nvarchar(max) — SQL Server cannot index that, so the declared index was never
            // actually created and every refresh table-scanned.
            entity.Property(rt => rt.Token).HasMaxLength(128);
            entity.Property(rt => rt.ReplacedByToken).HasMaxLength(128);
            entity.Property(rt => rt.DeviceInfo).HasMaxLength(256);
            entity.Property(rt => rt.IpAddress).HasMaxLength(64);

            // Unique: the token hash is the lookup key in RefreshAsync, and two rows sharing one
            // hash would make rotation and reuse detection ambiguous.
            entity.HasIndex(rt => rt.Token).IsUnique();
            entity.HasOne(rt => rt.User)
                  .WithMany()
                  .HasForeignKey(rt => rt.UserId);
        });

        modelBuilder.Entity<Wallet>(entity =>
        {
            entity.HasIndex(w => w.UserId).IsUnique();
            entity.Property(w => w.Balance).HasPrecision(18, 2);
            entity.HasOne(w => w.User)
                  .WithMany()
                  .HasForeignKey(w => w.UserId);
        });

        modelBuilder.Entity<WalletTransaction>(entity =>
        {
            entity.Property(wt => wt.Amount).HasPrecision(18, 2);
            entity.Property(wt => wt.BalanceBefore).HasPrecision(18, 2);
            entity.Property(wt => wt.BalanceAfter).HasPrecision(18, 2);

            // Both of these were unbounded and therefore nvarchar(max), which SQL Server will not
            // index — the same shape as the RefreshTokens.Token bug above. Bounding them is what
            // makes the index below possible at all, not a separate tidy-up. 32 clears the longest
            // WalletTransactionType name (TicketPurchase, 14); 64 clears a stringified Purchase.Id
            // with room for whatever a future reference scheme uses.
            entity.Property(wt => wt.Type).HasMaxLength(32);
            entity.Property(wt => wt.ReferenceId).HasMaxLength(64);

            // TicketingManager.RefundAsync probes for an existing refund under UPDLOCK/HOLDLOCK
            // before crediting. Without this index that range lock is taken over a full table scan,
            // so every refund serialises against every other and blocks inserts of any wallet
            // transaction for the length of its transaction. Unique, so it is also the backstop: a
            // writer that somehow got past the probe still collides on insert.
            //
            // Filtered, because only Refund rows carry the constraint — other types are free to
            // reuse a ReferenceId. The filter compares Type to the enum *name*, which the
            // EnumToStringConverter loop at the top of this method guarantees for every enum
            // property in the model. If that convention ever goes, this filter matches nothing and
            // the uniqueness is lost silently, with no error to notice.
            entity.HasIndex(wt => new { wt.Type, wt.ReferenceId })
                  .IsUnique()
                  .HasFilter("[ReferenceId] IS NOT NULL AND [Type] = 'Refund'");

            entity.HasOne(wt => wt.Wallet)
                  .WithMany(w => w.Transactions)
                  .HasForeignKey(wt => wt.WalletId);
        });

        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasIndex(us => us.UserId).IsUnique();
            entity.HasOne(us => us.User)
                  .WithMany()
                  .HasForeignKey(us => us.UserId);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(al => al.CreatedAt);
            entity.HasOne(al => al.User)
                  .WithMany()
                  .HasForeignKey(al => al.UserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TicketingAdapter>(entity =>
        {
            entity.HasIndex(ta => ta.TransitInfoGlobalId);
            entity.Property(ta => ta.ApiKeyEncrypted).HasMaxLength(500);
        });

        modelBuilder.Entity<TicketOption>(entity =>
        {
            entity.HasIndex(to => to.ExternalProductId);
            entity.Property(to => to.Price).HasPrecision(18, 2);
            entity.HasOne(to => to.Adapter)
                  .WithMany(ta => ta.TicketOptions)
                  .HasForeignKey(to => to.TicketingAdapterId);
        });

        modelBuilder.Entity<Purchase>(entity =>
        {
            entity.HasIndex(p => p.UserId);
            entity.HasIndex(p => p.ExternalPurchaseId);

            // The reconciliation sweep looks for Pending purchases older than a cutoff, and the
            // admin console asks for the oldest one. Without this the sweep table-scans Purchases
            // on every pass, and it runs far more often than any user-facing query.
            //
            // The length is explicit because the status is stored as its enum name: left unbounded
            // it is nvarchar(max), which cannot be indexed at all, and EF's fallback of 450 declares
            // a 900-byte ceiling over four short words.
            //
            // This comment used to say the 450 would "put ~900 bytes per row in an index". It does
            // not — nvarchar stores the length of the value, not the declared maximum, so the width
            // costs no storage. Measured in docs/changelog.md, 2026-08-15. The bound is still worth
            // declaring: it rejects nonsense at the column and leaves index-key budget for a
            // composite. It just is not a size saving.
            entity.Property(p => p.Status).HasMaxLength(20);
            entity.HasIndex(p => new { p.Status, p.PurchasedAt });

            // Filtered unique index: a retried purchase with the same key must collide rather than
            // charge the wallet a second time. Filtered so rows without a key are unconstrained.
            entity.Property(p => p.IdempotencyKey).HasMaxLength(64);
            entity.HasIndex(p => new { p.UserId, p.IdempotencyKey })
                  .IsUnique()
                  .HasFilter("[IdempotencyKey] IS NOT NULL");

            entity.Property(p => p.Amount).HasPrecision(18, 2);
            entity.Property(p => p.Currency).HasMaxLength(3);
            entity.HasOne(p => p.User)
                  .WithMany()
                  .HasForeignKey(p => p.UserId);
            entity.HasOne(p => p.Adapter)
                  .WithMany()
                  .HasForeignKey(p => p.TicketingAdapterId);
            entity.HasOne(p => p.TicketOption)
                  .WithMany()
                  .HasForeignKey(p => p.TicketOptionId);
            entity.HasOne(p => p.WalletTransaction)
                  .WithMany()
                  .HasForeignKey(p => p.WalletTransactionId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.HasIndex(t => t.ExternalTicketId);

            // The hourly expiry sweep selects on Status plus ValidTo across the whole table. There
            // was no index leading with Status here at all, so it scanned every ticket ever sold.
            entity.Property(t => t.Status).HasMaxLength(20);
            entity.HasIndex(t => new { t.Status, t.ValidTo });
            entity.HasOne(t => t.Purchase)
                  .WithMany()
                  .HasForeignKey(t => t.PurchaseId);
        });
        modelBuilder.Entity<ImportedTicket>(entity =>
        {
            entity.HasIndex(t => new { t.UserId, t.Status });

            // As above: the sweep is not per-user, so the (UserId, Status) index cannot serve it.
            entity.HasIndex(t => new { t.Status, t.ValidTo });

            entity.HasIndex(t => new { t.UserId, t.DedupeHash })
                  .IsUnique()
                  .HasFilter("[Status] = 'Active' AND [DedupeHash] IS NOT NULL");

            // Idempotency for the offline import queue. Filtered on NOT NULL because SQL Server
            // treats NULLs as equal in a unique index — unfiltered, this would permit exactly one
            // server-created ticket per user. Unlike the dedupe index above it is deliberately *not*
            // filtered on Status: the whole point is that a retry finds the original whatever became
            // of it, including a ticket already marked used.
            entity.HasIndex(t => new { t.UserId, t.ClientId })
                  .IsUnique()
                  .HasFilter("[ClientId] IS NOT NULL");
            entity.Property(t => t.Price).HasPrecision(18, 2);
            entity.Property(t => t.OperatorGlobalId).HasMaxLength(128);
            entity.Property(t => t.OperatorNameSnapshot).HasMaxLength(200);
            entity.Property(t => t.TicketName).HasMaxLength(200);
            entity.Property(t => t.RouteDescription).HasMaxLength(500);
            entity.Property(t => t.OriginName).HasMaxLength(200);
            entity.Property(t => t.DestinationName).HasMaxLength(200);
            entity.Property(t => t.Currency).HasMaxLength(3);
            entity.Property(t => t.SourceFileContentType).HasMaxLength(100);
            entity.Property(t => t.DedupeHash).HasMaxLength(64);
            entity.Property(t => t.RawPayload).HasMaxLength(8000);
            entity.Property(t => t.Status).HasMaxLength(32);
            entity.Property(t => t.Source).HasMaxLength(32);
            entity.Property(t => t.Verification).HasMaxLength(32);
            entity.HasOne(t => t.User)
                  .WithMany()
                  .HasForeignKey(t => t.UserId);
        });
        modelBuilder.Entity<Journey>(entity =>
        {
            // Drives the list view: a user's journeys, newest trip first.
            entity.HasIndex(j => new { j.UserId, j.StartsAt });
            entity.HasIndex(j => new { j.UserId, j.Status });

            entity.Property(j => j.Name).HasMaxLength(200);
            entity.Property(j => j.Notes).HasMaxLength(2000);
            entity.Property(j => j.Status).HasMaxLength(32);

            entity.HasOne(j => j.User)
                  .WithMany()
                  .HasForeignKey(j => j.UserId);

            // Deleting a journey releases its tickets rather than being blocked by them or taking
            // them down with it — the tickets are the valuable thing, the grouping is not. This
            // deliberately overrides the Restrict that the convention loop above applies to every
            // foreign key.
            entity.HasMany(j => j.ImportedTickets)
                  .WithOne(t => t.Journey)
                  .HasForeignKey(t => t.JourneyId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(j => j.Tickets)
                  .WithOne(t => t.Journey)
                  .HasForeignKey(t => t.JourneyId)
                  .OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<TicketUpload>(entity =>
        {
            // The blob key is the credential a client presents to attach a stored file to a new
            // ticket, so it has to resolve to exactly one row. Unique rather than merely indexed.
            entity.HasIndex(u => u.BlobKey).IsUnique();

            // Drives both the ownership lookup on create and the expiry sweep for uploads that
            // were never turned into a ticket.
            entity.HasIndex(u => new { u.UserId, u.ConsumedAt });

            entity.Property(u => u.BlobKey).HasMaxLength(128);
            entity.Property(u => u.ContentType).HasMaxLength(100);
            entity.Property(u => u.FileType).HasMaxLength(32);
            entity.HasOne(u => u.User)
                  .WithMany()
                  .HasForeignKey(u => u.UserId);
        });
    }
}
