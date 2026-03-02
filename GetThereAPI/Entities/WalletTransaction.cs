using GetThereShared.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace GetThereAPI.Entities
{
    public class WalletTransaction
    {
        public int Id { get; set; }
        public WalletTransactionType Type { get; set; }

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
