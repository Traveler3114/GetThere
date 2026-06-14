namespace GetThereAPI.Models;

public class PurchaseRequest
{
    public int TicketingAdapterId { get; set; }
    public int TicketOptionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? PaymentReference { get; set; }
}
