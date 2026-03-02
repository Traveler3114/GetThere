using GetThereShared.Enums;

namespace GetThereShared.Models
{
    public class PaymentDto
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public PaymentStatus? Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public int WalletId { get; set; }
        public int? PaymentProviderId { get; set; }
        public string? ProviderTransactionId { get; set; }
    }
}