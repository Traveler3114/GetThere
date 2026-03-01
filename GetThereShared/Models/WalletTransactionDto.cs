namespace GetThereShared.Models
{
    public class WalletTransactionDto
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Description { get; set; }
        public int WalletId { get; set; }
        public int? TicketId { get; set; }
    }
}
