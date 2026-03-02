using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using GetThereAPI.Models;

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
        public DbSet<PaymentProvider> PaymentProviders { get; set; }

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
        }
    }
}