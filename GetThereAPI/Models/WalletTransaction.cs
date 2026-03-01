using System.ComponentModel.DataAnnotations.Schema;

namespace GetThereAPI.Models
{
    public class WalletTransaction
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty; // topup, ticket_purchase, refund

        [Column(TypeName = "decimal(16,2)")]
        public decimal Amount { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? Description { get; set; }

        public int WalletId { get; set; }
        public Wallet Wallet { get; set; } = null!;

        public int? TicketId { get; set; }
        public Ticket? Ticket { get; set; }
    }
}
