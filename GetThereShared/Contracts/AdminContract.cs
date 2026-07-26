namespace GetThereShared.Contracts;

public class UserListItem
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
}

/// <summary>Aggregates shown on the admin overview KPI row.</summary>
public class AdminStats
{
    public string Currency { get; set; } = "EUR";

    public int TicketsSold { get; set; }
    public double TicketsSoldChangePercent { get; set; }
    /// <summary>Purchase counts per day, oldest first, ending with the current day.</summary>
    public List<int> TicketsSoldDaily { get; set; } = [];

    public decimal GrossVolume { get; set; }
    public double GrossVolumeChangePercent { get; set; }
    public decimal AverageBasket { get; set; }
    public int Refunds { get; set; }

    public decimal WalletFloat { get; set; }
    public decimal TopUps { get; set; }
    public decimal Spend { get; set; }

    public double PurchaseSuccessRate { get; set; }
    public double PurchaseSuccessRateChangePercent { get; set; }

    public int PendingPurchases { get; set; }
    public DateTime? OldestPendingPurchaseAt { get; set; }

    public int TotalUsers { get; set; }
    public int TotalTickets { get; set; }
    public int AdaptersDegraded { get; set; }
    public int AdaptersWithoutOptions { get; set; }
}

/// <summary>One row of the admin overview purchase feed.</summary>
public class PurchaseListItem
{
    public int Id { get; set; }
    public int? TicketId { get; set; }
    public string? ExternalTicketId { get; set; }
    public string? UserEmail { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string AdapterType { get; set; } = string.Empty;
    public string OptionName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string PaymentStatus { get; set; } = string.Empty;
    public string? TicketStatus { get; set; }
    public DateTime PurchasedAt { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>Health and configuration of one registered ticketing adapter.</summary>
public class AdapterHealthItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AdapterType { get; set; } = string.Empty;
    public string TransitInfoGlobalId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool HasApiKey { get; set; }
    /// <summary>True when an SDK implementation is registered for <see cref="AdapterType"/>.</summary>
    public bool IsRegistered { get; set; }
    public List<string> RequiredInputs { get; set; } = [];
    public int TicketOptions { get; set; }
    public int Purchases { get; set; }
    public int Failures { get; set; }
    public int Pending { get; set; }
    public decimal Volume { get; set; }
    public DateTime? LastPurchaseAt { get; set; }
    /// <summary>One of: Ok, Degraded, Failing, Unregistered, Disabled, Idle.</summary>
    public string Status { get; set; } = "Idle";
}

public class AuditLogEntry
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime CreatedAt { get; set; }
}
