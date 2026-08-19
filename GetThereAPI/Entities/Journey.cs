using GetThereShared.Enums;

namespace GetThereAPI.Entities;

/// <summary>
/// A trip: several tickets that belong together, such as an outbound rail leg, a city transit pass
/// for the middle, and the return.
/// <para>
/// Membership is a nullable <c>JourneyId</c> on each ticket table rather than a polymorphic join
/// table, so the database enforces it — and it finally gives
/// <see cref="ImportedTicket.JourneyId"/> the foreign key it was named for but never had.
/// </para>
/// </summary>
public class Journey
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public JourneyStatus Status { get; set; } = JourneyStatus.Planned;

    /// <summary>
    /// Derived from the member tickets, but stored so journeys can be sorted and filtered without
    /// joining every ticket table on every list query. Recalculated whenever membership changes.
    /// </summary>
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ImportedTicket> ImportedTickets { get; set; } = [];
    public ICollection<Ticket> Tickets { get; set; } = [];

    /// <summary>Buy-on-board legs held for this journey (funds reserved, ticket bought on board).</summary>
    public ICollection<JourneyReservation> Reservations { get; set; } = [];
}
