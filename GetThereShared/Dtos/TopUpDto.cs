namespace GetThereShared.Dtos
{
    public class TopUpDto
    {
        public string UserId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public int? PaymentProviderId { get; set; }
    }
}