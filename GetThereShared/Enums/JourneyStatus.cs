namespace GetThereShared.Enums;

/// <summary>
/// Where a journey sits relative to now. Rolled forward from its member tickets' dates by the
/// expiry worker rather than set by hand, so it cannot drift from the legs it describes.
/// </summary>
public enum JourneyStatus
{
    /// <summary>Every leg is still ahead.</summary>
    Planned,
    /// <summary>Under way — the first leg has started and the last has not finished.</summary>
    Active,
    /// <summary>The last leg has passed.</summary>
    Completed,
    /// <summary>Abandoned by the user. Members are released rather than cancelled with it.</summary>
    Cancelled
}
