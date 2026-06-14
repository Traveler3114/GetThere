using GetThereShared.Enums;

namespace GetThereAPI.Models;

public class PurchaseResult
{
    public bool Success { get; set; }
    public string? ExternalPurchaseId { get; set; }
    public TicketPayload? Ticket { get; set; }
    public string? ErrorMessage { get; set; }
}
