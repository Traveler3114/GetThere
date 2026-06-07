using GetThereShared.Enums;

namespace GetThereShared.Contracts;

public class TicketableOperatorResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public OperatorType Type { get; set; }
    public string Color { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public bool IsMock { get; set; }
}

public class TicketOptionResponse
{
    public string OptionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Validity { get; set; } = string.Empty;
}

public class TicketPurchaseResponse
{
    public string TicketId { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string TicketName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string ValidFrom { get; set; } = string.Empty;
    public string ValidUntil { get; set; } = string.Empty;
    public string QrCodeData { get; set; } = string.Empty;
    public bool IsMock { get; set; } = true;
}

public record PurchaseTicketRequest
{
    public string OptionId { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
}
