using GetThere.Localization;
using GetThere.Pages;
using GetThere.Services;

namespace GetThere;

public partial class AppShell : Shell
{
    /// <summary>
    /// One navigation destination, in the order the design lists them.
    /// <para>
    /// <b><c>DesktopOnly</c> is vestigial.</b> It described a Settings destination that was
    /// desktop-only — the phone frames had no room for a fifth tab — and that destination is gone:
    /// Settings was folded into Profile → Account on every idiom. No entry in
    /// <see cref="Destinations"/> sets it, so the <c>Where</c> filter in <c>BuildNavigation</c>
    /// removes nothing and the desktop and phone lists are identical.
    /// </para>
    /// </summary>
    private sealed record NavItem(
        string TitleKey,
        string LightIcon,
        string DarkIcon,
        string Route,
        Type PageType,
        bool DesktopOnly = false);

    private static readonly NavItem[] Destinations =
    [
        new("Tab_Profile", "profile.png", "profile_white.png", "profile", typeof(ProfilePage)),
        new("Tab_Map", "map.png", "map_white.png", "map", typeof(MapPage)),
        new("Tab_Shop", "shop_bag.png", "shop_bag_white.png", "shop", typeof(ShopPage)),
        new("Tab_Tickets", "ticket.png", "ticket_white.png", "tickets", typeof(TicketsPage))
    ];

    public AppShell(IAnalyticsService analytics)
    {
        InitializeComponent();
        BuildNavigation();

        Navigated += (s, e) =>
        {
            if (e.Current?.Location is not null)
                analytics.TrackScreen(e.Current.Location.OriginalString);
        };

        Routing.RegisterRoute("importticket", typeof(ImportTicketPage));
        Routing.RegisterRoute("ticketpurchase", typeof(TicketPurchasePage));
        Routing.RegisterRoute("ticketdetail", typeof(TicketDetailPage));

        // Imported tickets are a separate route rather than a mode of the one above: the two
        // contracts share no base type and their detail screens show different fields.
        Routing.RegisterRoute("importedticketdetail", typeof(ImportedTicketDetailPage));

        // Journeys have no destination of their own: the design puts them behind a segmented
        // control on Tickets rather than a fifth tab, which the phone frames have no room for.
        Routing.RegisterRoute("journeydetail", typeof(JourneyDetailPage));

        // "Buy a journey" — reached from the map's gtapp://journey WebView handoff, never from a tab.
        Routing.RegisterRoute("buyjourney", typeof(BuyJourneyPage));
    }

    private void BuildNavigation()
    {
        var isDesktop = DeviceInfo.Idiom == DeviceIdiom.Desktop;

        if (isDesktop)
        {
            // Locked flyout = the permanent side rail the desktop frames draw.
            FlyoutBehavior = FlyoutBehavior.Locked;
            FlyoutWidth = 220;

            foreach (var item in Destinations)
                Items.Add(new FlyoutItem
                {
                    Title = LocalizationService.Instance[item.TitleKey],
                    Route = item.Route,
                    Items = { BuildContent(item) }
                });

            return;
        }

        FlyoutBehavior = FlyoutBehavior.Disabled;

        var tabBar = new TabBar();
        foreach (var item in Destinations.Where(d => !d.DesktopOnly))
            tabBar.Items.Add(BuildContent(item));

        Items.Add(tabBar);
    }

    private static ShellContent BuildContent(NavItem item)
    {
        var content = new ShellContent
        {
            Title = LocalizationService.Instance[item.TitleKey],
            Route = item.Route,
            ContentTemplate = new DataTemplate(item.PageType)
        };

        content.SetAppTheme<ImageSource>(ShellContent.IconProperty, item.LightIcon, item.DarkIcon);
        return content;
    }

    /// <summary>
    /// Swaps the Profile tab's icon for the user's own picture.
    /// <para>
    /// Looks the item up by route rather than by position — the tree is a TabBar on phones and a
    /// list of FlyoutItems on desktop, so a fixed index would find the wrong item on one of them.
    /// </para>
    /// <para>
    /// <b>Nothing calls it</b>, so the Profile tab always shows the static icon — and nothing can,
    /// yet: <c>ProfilePage.OnAvatarClicked</c> offers "Take Photo" / "Upload" and then answers
    /// either choice with <c>Profile_PhotoResult</c>, whose text is "Camera/Gallery integration
    /// would go here." There is no picker and no stored image for this method to be given.
    /// <para>
    /// The lookup it describes is correct and worth keeping for when there is. Noted because
    /// <c>getthere-client/architecture.md</c> gives this method a section of its own, which reads as
    /// documentation of a working feature.
    /// </para>
    /// </para>
    /// </summary>
    public void UpdateProfileIcon(ImageSource? source)
    {
        var profileItem = Items
            .SelectMany(section => section.Items)
            .SelectMany(group => group.Items)
            .FirstOrDefault(content => content.Route == "profile");

        if (profileItem is not null)
            profileItem.Icon = source ?? "profile.png";
    }
}
