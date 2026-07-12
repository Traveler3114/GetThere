namespace TransitInfoAPI.Common;

public static class PermissionKeys
{
    public const string FeedsView = "feeds.view";
    public const string FeedsManage = "feeds.manage";

    public const string FeedVersionsView = "feedversions.view";

    public const string OperatorsView = "operators.view";
    public const string OperatorsManage = "operators.manage";

    public const string StationsView = "stations.view";
    public const string StationsManage = "stations.manage";

    public const string RoutesView = "routes.view";
    public const string RoutesManage = "routes.manage";

    public const string RealtimeView = "realtime.view";

    public const string MobilityView = "mobility.view";

    public const string AgenciesView = "agencies.view";

    public const string PlacesView = "places.view";

    public const string CountriesView = "countries.view";
    public const string CountriesManage = "countries.manage";

    public const string ReconciliationView = "reconciliation.view";
    public const string ReconciliationManage = "reconciliation.manage";

    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";

    public const string RolesView = "roles.view";
    public const string RolesManage = "roles.manage";

    public static readonly string[] All =
    [
        FeedsView, FeedsManage, FeedVersionsView,
        OperatorsView, OperatorsManage, StationsView, StationsManage,
        RoutesView, RoutesManage, RealtimeView, MobilityView,
        AgenciesView, PlacesView, CountriesView, CountriesManage,
        ReconciliationView, ReconciliationManage, UsersView, UsersManage,
        RolesView, RolesManage
    ];

    public static readonly Dictionary<string, (string DisplayName, string Description, string Category)> Meta = new()
    {
        [FeedsView] = ("View Feeds", "List and view GTFS feeds", "Feeds"),
        [FeedsManage] = ("Manage Feeds", "Create, update, delete, import feeds", "Feeds"),
        [FeedVersionsView] = ("View Feed Versions", "List feed versions and logs", "Feeds"),
        [OperatorsView] = ("View Operators", "List and view operators", "Operators"),
        [OperatorsManage] = ("Manage Operators", "Create, update, delete operators", "Operators"),
        [StationsView] = ("View Stations", "List and view stations", "Stations"),
        [StationsManage] = ("Manage Stations", "Rematch places, reconciliation detail", "Stations"),
        [RoutesView] = ("View Routes", "List and view routes", "Routes"),
        [RoutesManage] = ("Manage Routes", "Update route shapes", "Routes"),
        [RealtimeView] = ("View Realtime", "View vehicles and alerts", "Realtime"),
        [MobilityView] = ("View Mobility", "View mobility stations and countries", "Mobility"),
        [AgenciesView] = ("View Agencies", "List agencies", "Agencies"),
        [PlacesView] = ("View Places", "List places and operators", "Places"),
        [CountriesView] = ("View Countries", "List countries", "Countries"),
        [CountriesManage] = ("Manage Countries", "Create countries", "Countries"),
        [ReconciliationView] = ("View Reconciliation", "View pending/auto-merged candidates", "Reconciliation"),
        [ReconciliationManage] = ("Manage Reconciliation", "Approve, reject, merge, reassign", "Reconciliation"),
        [UsersView] = ("View Users", "List admin users", "Admin"),
        [UsersManage] = ("Manage Users", "Create users, assign roles", "Admin"),
        [RolesView] = ("View Roles", "List roles and permissions", "Admin"),
        [RolesManage] = ("Manage Roles", "Create roles, assign permissions", "Admin"),
    };
}