using GetThereShared.Enums;

namespace GetThereShared.Models
{
    public class TicketDto
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string TicketType { get; set; } = string.Empty;
        public DateTime PurchasedAt { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidUntil { get; set; }
        public TicketFormat? Format { get; set; }
        public string? Payload { get; set; }
        public string? DisplayInstructions { get; set; }
        public TicketStatus Status { get; set; } = TicketStatus.Active;

        public int? TransitOperatorId { get; set; }
    }
}