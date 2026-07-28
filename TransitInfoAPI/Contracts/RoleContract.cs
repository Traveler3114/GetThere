namespace TransitInfoAPI.Contracts;

/// <summary>
/// A role and the permissions it carries.
/// <para>
/// Deliberately declared here rather than shared with GetThereAPI. The two services have separate
/// user stores, separate permission vocabularies (<see cref="Common.PermissionKeys"/> here lists
/// feeds, stations and reconciliation; GetThereAPI's lists wallets, tickets and journeys) and
/// separate roles — <c>Admin</c>/<c>Client</c> here, <c>Admin</c>/<c>User</c> there. That the two
/// shapes currently match is a coincidence of both having grown the same admin screen, not a
/// contract; sharing the type would couple two independent release cycles for no benefit and let a
/// change made for one service silently alter the other.
/// </para>
/// </summary>
public class RoleDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Values from <see cref="Common.PermissionKeys"/>.</summary>
    public List<string> Permissions { get; set; } = [];
}

/// <summary>
/// An account in this service's own user store: an administrator, or a service account such as the
/// one GetThereAPI authenticates with. There are no end users here.
/// </summary>
public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }

    /// <summary>Currently always true — nothing sets an account inactive.</summary>
    public bool IsActive { get; set; }
}
