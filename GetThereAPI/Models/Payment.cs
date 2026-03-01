using System.ComponentModel.DataAnnotations.Schema;

namespace GetThereAPI.Models
{
    public class Payment
    {
        public int Id { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string? ProviderTransactionId { get; set; }

        [Column(TypeName = "decimal(16,2)")]
        public decimal Amount { get; set; }

        public string? Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int WalletId { get; set; }
        public Wallet Wallet { get; set; } = null!;
    }
}
