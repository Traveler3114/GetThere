using System.ComponentModel.DataAnnotations.Schema;
using GetThereShared.Enums;

namespace GetThereAPI.Models
{
    public class Payment
    {
        public int Id { get; set; }

        [Column(TypeName = "decimal(16,2)")]
        public decimal Amount { get; set; }

        public PaymentStatus? Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int WalletId { get; set; }
        public Wallet Wallet { get; set; } = null!;

        public int? PaymentProviderId { get; set; }
        public PaymentProvider? PaymentProvider { get; set; }
        public string? ProviderTransactionId { get; set; }
    }
}