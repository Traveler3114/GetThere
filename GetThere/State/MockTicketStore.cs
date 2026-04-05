using GetThereShared.Dtos;

namespace GetThere.State;

/// <summary>
/// In-memory store for mock tickets purchased during the current app session.
/// This is a singleton so tickets are visible anywhere in the app without
/// requiring network calls, and without needing the user to be logged in.
/// </summary>
public class MockTicketStore
{
    private readonly List<MockTicketResultDto> _tickets = [];

    /// <summary>All mock tickets purchased in this session, newest first.</summary>
    public IReadOnlyList<MockTicketResultDto> Tickets
        => [.. _tickets.OrderByDescending(t => t.ValidFrom)];

    /// <summary>Adds a newly purchased mock ticket to the session store.</summary>
    public void Add(MockTicketResultDto ticket)
        => _tickets.Add(ticket);

    /// <summary>Removes all tickets from the session store.</summary>
    public void Clear()
        => _tickets.Clear();
}
