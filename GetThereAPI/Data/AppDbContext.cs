using GetThereAPI.Entities;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GetThereAPI.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<Wallet> Wallets { get; set; }
    public DbSet<WalletTransaction> WalletTransactions { get; set; }
    public DbSet<UserSettings> UserSettings { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<TicketingAdapter> TicketingAdapters { get; set; }
    public DbSet<TicketOption> TicketOptions { get; set; }
    public DbSet<Purchase> Purchases { get; set; }
    public DbSet<Ticket> Tickets { get; set; }
    public DbSet<ImportedTicket> ImportedTickets { get; set; }

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
            entity.HasOne(t => t.Purchase)
                  .WithMany()
                  .HasForeignKey(t => t.PurchaseId);
        });
        modelBuilder.Entity<ImportedTicket>(entity =>
        {
            entity.HasIndex(t => new { t.UserId, t.Status });
            entity.HasIndex(t => new { t.UserId, t.DedupeHash })
                  .IsUnique()
                  .HasFilter("[Status] = 'Active' AND [DedupeHash] IS NOT NULL");
            entity.Property(t => t.Price).HasPrecision(18, 2);
            entity.Property(t => t.OperatorGlobalId).HasMaxLength(128);
            entity.Property(t => t.OperatorNameSnapshot).HasMaxLength(200);
            entity.Property(t => t.TicketName).HasMaxLength(200);
            entity.Property(t => t.RouteDescription).HasMaxLength(500);
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
    }
}
