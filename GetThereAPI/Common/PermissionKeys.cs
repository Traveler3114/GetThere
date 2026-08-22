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

    // Imported Tickets
    public const string ImportedTicketsView = "importedtickets.view";
    public const string ImportedTicketsCreate = "importedtickets.create";
    public const string ImportedTicketsManage = "importedtickets.manage";

    // Journeys — grouping tickets into a trip
    public const string JourneysView = "journeys.view";
    public const string JourneysCreate = "journeys.create";
    public const string JourneysManage = "journeys.manage";

    // Wallets
    public const string WalletsView = "wallets.view";
    public const string WalletsManage = "wallets.manage";

    /// <summary>
    /// Credits a wallet. Deliberately NOT in <see cref="UserRoleDefaults"/>: no payment provider is
    /// wired up, so this mints balance outright. Move it into the User role only once top-up takes
    /// real money — see docs/money-path-defects.md.
    /// </summary>
    public const string WalletsTopUp = "wallets.topup";

    // Profile
    public const string ProfileView = "profile.view";
    public const string ProfileManage = "profile.manage";

    // Settings
    public const string SettingsView = "settings.view";
    public const string SettingsManage = "settings.manage";

    // Adapters (ticketing)
    public const string AdaptersView = "adapters.view";
    public const string AdaptersManage = "adapters.manage";

    // Admin console — platform-wide views. Never grant these to the User role:
    // they expose every user's purchases and the platform's financial totals.
    public const string AdminStatsView = "admin.stats.view";
    public const string AdminPurchasesView = "admin.purchases.view";

    // Audit
    public const string AuditView = "audit.view";

    public static readonly string[] All =
    [
        UsersView, UsersManage, RolesView, RolesManage,
        TicketsView, TicketsCreate, TicketsManage,
        ImportedTicketsView, ImportedTicketsCreate, ImportedTicketsManage,
        JourneysView, JourneysCreate, JourneysManage,
        WalletsView, WalletsManage, WalletsTopUp,
        ProfileView, ProfileManage,
        SettingsView, SettingsManage,
        AdaptersView, AdaptersManage,
        AdminStatsView, AdminPurchasesView,
        AuditView
    ];

    /// <summary>
    /// Permissions granted to the <see cref="RoleNames.User"/> role at startup. Anything listed here
    /// is held by every registered account, so an endpoint gated on one of these is effectively
    /// public to authenticated users — never use one to guard platform-wide or cross-user data.
    /// </summary>
    public static readonly string[] UserRoleDefaults =
    [
        TicketsView, TicketsCreate,
        WalletsView, WalletsManage,
        ProfileView, ProfileManage,
        SettingsView, SettingsManage,
        ImportedTicketsView, ImportedTicketsCreate, ImportedTicketsManage,
        JourneysView, JourneysCreate, JourneysManage
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
        [ImportedTicketsView] = ("View Imported Tickets", "List and view imported tickets", "Tickets"),
        [ImportedTicketsCreate] = ("Import Tickets", "Import tickets via manual entry, photo, PDF, or scan", "Tickets"),
        [ImportedTicketsManage] = ("Manage Imported Tickets", "Update status, cancel imported tickets", "Tickets"),
        [JourneysView] = ("View Journeys", "List and view journeys", "Tickets"),
        [JourneysCreate] = ("Create Journeys", "Group tickets into a journey", "Tickets"),
        [JourneysManage] = ("Manage Journeys", "Rename, add and remove tickets, delete journeys", "Tickets"),
        [WalletsView] = ("View Wallet", "View wallet balance and transactions", "Wallet"),
        [WalletsManage] = ("Manage Wallet", "Manage own wallet", "Wallet"),
        [WalletsTopUp] = ("Top Up Wallet", "Credit a wallet balance — no payment provider is wired up yet", "Wallet"),
        [ProfileView] = ("View Profile", "View own profile", "Profile"),
        [ProfileManage] = ("Manage Profile", "Update profile, change password", "Profile"),
        [SettingsView] = ("View Settings", "View app settings", "Settings"),
        [SettingsManage] = ("Manage Settings", "Update app settings", "Settings"),
        [AdaptersView] = ("View Adapters", "List ticketing adapters", "Adapters"),
        [AdaptersManage] = ("Manage Adapters", "Create, update, delete ticketing adapters", "Adapters"),
        [AdminStatsView] = ("View Platform Stats", "View platform-wide KPIs, revenue and wallet float", "Admin"),
        [AdminPurchasesView] = ("View All Purchases", "View the purchase feed across all users", "Admin"),
        [AuditView] = ("View Audit Log", "View audit log entries", "Audit"),
    };
}
