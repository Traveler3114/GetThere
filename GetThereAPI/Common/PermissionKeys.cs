namespace GetThereAPI.Common;

public static class PermissionKeys
{
    // Users & Roles
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string RolesView = "roles.view";
    public const string RolesManage = "roles.manage";

    // Tickets
    public const string TicketsView = "tickets.view";
    public const string TicketsCreate = "tickets.create";
    public const string TicketsManage = "tickets.manage";

    // Wallets
    public const string WalletsView = "wallets.view";
    public const string WalletsManage = "wallets.manage";

    // Profile
    public const string ProfileView = "profile.view";
    public const string ProfileManage = "profile.manage";

    // Settings
    public const string SettingsView = "settings.view";
    public const string SettingsManage = "settings.manage";

    // Adapters (ticketing)
    public const string AdaptersView = "adapters.view";
    public const string AdaptersManage = "adapters.manage";

    // Audit
    public const string AuditView = "audit.view";

    // Map (via MapProxyController)
    public const string MapView = "map.view";

    public static readonly string[] All =
    [
        UsersView, UsersManage, RolesView, RolesManage,
        TicketsView, TicketsCreate, TicketsManage,
        WalletsView, WalletsManage,
        ProfileView, ProfileManage,
        SettingsView, SettingsManage,
        AdaptersView, AdaptersManage,
        AuditView,
        MapView
    ];

    public static readonly Dictionary<string, (string DisplayName, string Description, string Category)> Meta = new()
    {
        [UsersView] = ("View Users", "List and view users", "Users"),
        [UsersManage] = ("Manage Users", "Create, update, delete users, assign roles", "Users"),
        [RolesView] = ("View Roles", "List roles and permissions", "Users"),
        [RolesManage] = ("Manage Roles", "Create roles, assign permissions", "Users"),
        [TicketsView] = ("View Tickets", "List and view tickets", "Tickets"),
        [TicketsCreate] = ("Create Tickets", "Purchase new tickets", "Tickets"),
        [TicketsManage] = ("Manage Tickets", "Refund, void, cancel tickets", "Tickets"),
        [WalletsView] = ("View Wallet", "View wallet balance and transactions", "Wallet"),
        [WalletsManage] = ("Manage Wallet", "Top up, withdraw, transfer", "Wallet"),
        [ProfileView] = ("View Profile", "View own profile", "Profile"),
        [ProfileManage] = ("Manage Profile", "Update profile, change password", "Profile"),
        [SettingsView] = ("View Settings", "View app settings", "Settings"),
        [SettingsManage] = ("Manage Settings", "Update app settings", "Settings"),
        [AdaptersView] = ("View Adapters", "List ticketing adapters", "Adapters"),
        [AdaptersManage] = ("Manage Adapters", "Create, update, delete ticketing adapters", "Adapters"),
        [AuditView] = ("View Audit Log", "View audit log entries", "Audit"),
        [MapView] = ("View Map", "Access map data (stations, routes, vehicles)", "Map"),
    };
}