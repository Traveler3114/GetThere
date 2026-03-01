namespace GetThereAPI.Models
{
    public class Ticket
    {
        public int Id { get; set; }
        public string TicketType { get; set; } = string.Empty;
        public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidUntil { get; set; }
        public string? Format { get; set; }
        public string? Payload { get; set; }
        public string? DisplayInstructions { get; set; }
        public string Status { get; set; } = "active";

        public string UserId { get; set; } = string.Empty;
        public AppUser User { get; set; } = null!;
    }
}
