namespace GetThereShared.Models
{
    public class PaymentDto
    {
        public int Id { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string? ProviderTransactionId { get; set; }
        public decimal Amount { get; set; }
        public string? Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public int WalletId { get; set; }
    }
}
