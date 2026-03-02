using GetThereShared.Enums;

namespace GetThereShared.Dtos
{
    public class WalletTransactionDto
    {
        public int Id { get; set; }
        public WalletTransactionType Type { get; set; }
        public decimal Amount { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Description { get; set; }
        public int WalletId { get; set; }
        public int? TicketId { get; set; }
    }
}
