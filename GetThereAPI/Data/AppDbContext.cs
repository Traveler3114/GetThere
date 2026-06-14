using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using GetThereAPI.Entities;

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
            entity.HasIndex(rt => rt.Token);
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
            entity.Property(p => p.Amount).HasPrecision(18, 2);
            entity.HasOne(p => p.User)
                  .WithMany()
                  .HasForeignKey(p => p.UserId);
            entity.HasOne(p => p.Adapter)
                  .WithMany()
                  .HasForeignKey(p => p.TicketingAdapterId);
            entity.HasOne(p => p.TicketOption)
                  .WithMany()
                  .HasForeignKey(p => p.TicketOptionId);
        });

        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.HasIndex(t => t.ExternalTicketId);
            entity.HasOne(t => t.Purchase)
                  .WithMany()
                  .HasForeignKey(t => t.PurchaseId);
        });
    }
}
