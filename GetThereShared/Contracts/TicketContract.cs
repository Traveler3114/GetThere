using GetThereShared.Enums;

namespace GetThereShared.Contracts;

public class TicketTypeResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EUR";
    public TicketFormat TicketFormat { get; set; }
    public int? DurationMinutes { get; set; }
    public int? ValidityDays { get; set; }
    public int? TransferCount { get; set; }
    public bool IsActive { get; set; }
}

public class TicketInstanceResponse
{
    public int Id { get; set; }
    public TicketTypeResponse TicketType { get; set; } = null!;
    public TicketStatus Status { get; set; }
    public DateTime PurchaseDate { get; set; }
    public DateTime? ActivationDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
}

public class PurchaseTicketRequest
{
    public int TicketTypeId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
}

public class TicketValidationResponse
{
    public int Id { get; set; }
    public string TicketIdentifier { get; set; } = string.Empty;
    public DateTime ValidatedAt { get; set; }
    public bool IsValid { get; set; }
    public string? FailureReason { get; set; }
}
