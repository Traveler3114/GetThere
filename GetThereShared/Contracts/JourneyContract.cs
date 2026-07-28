using System.ComponentModel.DataAnnotations;

using GetThereShared.Enums;

namespace GetThereShared.Contracts;

public class CreateJourneyRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Notes { get; set; }

    /// <summary>
    /// Imported tickets to place in the journey immediately, so accepting a suggestion is one
    /// request rather than a create followed by N adds.
    /// </summary>
    public List<int> ImportedTicketIds { get; set; } = [];

    /// <summary>Purchased tickets to place in the journey immediately.</summary>
    public List<int> TicketIds { get; set; } = [];
}

public class UpdateJourneyRequest
{
    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    /// <summary>Only <see cref="JourneyStatus.Cancelled"/> is settable by hand; the rest roll forward from the legs.</summary>
    public JourneyStatus? Status { get; set; }
}

/// <summary>Which tickets to move in or out of a journey.</summary>
public class JourneyMembershipRequest
{
    public List<int> ImportedTicketIds { get; set; } = [];
    public List<int> TicketIds { get; set; } = [];
}

/// <summary>One leg of a journey, flattened across the imported and purchased ticket tables.</summary>
public class JourneyLegResponse
{
    public int Id { get; set; }

    /// <summary>True when this leg is an imported ticket, false when it is a purchased one — the two share an id space only by accident.</summary>
    public bool IsImported { get; set; }

    public string? TicketName { get; set; }
    public string? RouteDescription { get; set; }
    public string? OriginName { get; set; }
    public string? DestinationName { get; set; }
    public string? OperatorNameSnapshot { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
}

public class JourneyResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public JourneyStatus Status { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int LegCount { get; set; }

    /// <summary>Populated on get-by-id, left empty on list responses so the list stays cheap.</summary>
    public List<JourneyLegResponse> Legs { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// A proposed grouping the user can accept or ignore. Never applied automatically — a wrong guess
/// would silently reshuffle someone's wallet.
/// </summary>
public class JourneySuggestionResponse
{
    /// <summary>Suggested journey name, derived from the first and last stop where those are known.</summary>
    public string SuggestedName { get; set; } = string.Empty;

    /// <summary>Why these tickets were grouped, in words the UI can show directly.</summary>
    public string Reason { get; set; } = string.Empty;

    public List<JourneyLegResponse> Legs { get; set; } = [];
    public List<int> ImportedTicketIds { get; set; } = [];
    public List<int> TicketIds { get; set; } = [];
}
