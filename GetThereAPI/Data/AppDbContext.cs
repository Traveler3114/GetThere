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
    public DbSet<TicketType> TicketTypes { get; set; }
    public DbSet<TicketInstance> TicketInstances { get; set; }
    public DbSet<TicketValidation> TicketValidations { get; set; }
    public DbSet<Contact> Contacts { get; set; }
    public DbSet<UserSettings> UserSettings { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

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
        }

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(rt => rt.Token);
            entity.HasOne(rt => rt.User)
                  .WithMany()
                  .HasForeignKey(rt => rt.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Wallet>(entity =>
        {
            entity.HasIndex(w => w.UserId).IsUnique();
            entity.Property(w => w.Balance).HasPrecision(18, 2);
            entity.HasOne(w => w.User)
                  .WithMany()
                  .HasForeignKey(w => w.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WalletTransaction>(entity =>
        {
            entity.Property(wt => wt.Amount).HasPrecision(18, 2);
            entity.Property(wt => wt.BalanceBefore).HasPrecision(18, 2);
            entity.Property(wt => wt.BalanceAfter).HasPrecision(18, 2);
            entity.HasOne(wt => wt.Wallet)
                  .WithMany(w => w.Transactions)
                  .HasForeignKey(wt => wt.WalletId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TicketType>(entity =>
        {
            entity.Property(tt => tt.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<TicketInstance>(entity =>
        {
            entity.HasIndex(ti => ti.UserId);
            entity.HasOne(ti => ti.User)
                  .WithMany()
                  .HasForeignKey(ti => ti.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(ti => ti.TicketType)
                  .WithMany()
                  .HasForeignKey(ti => ti.TicketTypeId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TicketValidation>(entity =>
        {
            entity.HasOne(tv => tv.TicketInstance)
                  .WithMany()
                  .HasForeignKey(tv => tv.TicketInstanceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasOne(c => c.User)
                  .WithMany()
                  .HasForeignKey(c => c.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasIndex(us => us.UserId).IsUnique();
            entity.HasOne(us => us.User)
                  .WithMany()
                  .HasForeignKey(us => us.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(al => al.CreatedAt);
            entity.HasOne(al => al.User)
                  .WithMany()
                  .HasForeignKey(al => al.UserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
