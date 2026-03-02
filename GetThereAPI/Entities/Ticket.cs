using GetThereShared.Enums;

namespace GetThereAPI.Entities
{
    public class Ticket
    {
        public int Id { get; set; }
        public string TicketType { get; set; } = string.Empty;
        public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidUntil { get; set; }
        public TicketFormat? Format { get; set; }
        public string? Payload { get; set; }
        public string? DisplayInstructions { get; set; }
        public TicketStatus Status { get; set; } = TicketStatus.Active;

        public string UserId { get; set; } = string.Empty;
        public AppUser User { get; set; } = null!;

        public int? TransitOperatorId { get; set; }
        public TransitOperator? TransitOperator { get; set; }
    }
}