namespace GetThereAPI.Models
{
    public class PaymentProvider
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public string ApiBaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string? WebhookSecret { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}