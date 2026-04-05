#nullable enable
using GetThere.State;

namespace GetThere;

public partial class AppShell : Shell
{
    private readonly string[] _ticketIcons = { "tab_train.png", "tab_bus.png", "tab_tram.png" };
    private int _ticketIconIndex;
    private readonly System.Timers.Timer _ticketIconTimer;

    public AppShell()
    {
        InitializeComponent();

        _ticketIconTimer = new System.Timers.Timer(2000);
        _ticketIconTimer.Elapsed += (_, _) => MainThread.BeginInvokeOnMainThread(RotateTicketIcon);
        Navigated += (_, _) => UpdateTicketIconState();

        UpdateTicketIconState();

        // On first launch, navigate to Settings so the user can pick a country
        Loaded += OnShellLoaded;
    }

    private async void OnShellLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnShellLoaded;

        var prefs = Handler?.MauiContext?.Services.GetService<CountryPreferenceService>();
        if (prefs is not null && !prefs.HasSelection)
        {
            await Task.Delay(400); // allow shell to settle
            await GoToAsync("//settings");
        }
    }

    public void UpdateProfileIcon(ImageSource? source)
    {
        var profileTab = this.FindByName<ShellContent>("ProfileTab");
        if (profileTab == null) return;

        profileTab.Icon = source ?? "icon_profile.svg";
    }

    private void UpdateTicketIconState()
    {
        var ticketsTab = this.FindByName<ShellContent>("TicketsTab");
        if (ticketsTab == null) return;

        var isTicketsRoute = Shell.Current?.CurrentState?.Location?.ToString().Contains("tickets", StringComparison.OrdinalIgnoreCase) == true;

        if (isTicketsRoute)
        {
            _ticketIconTimer.Start();
            ticketsTab.Icon = _ticketIcons[_ticketIconIndex];
        }
        else
        {
            _ticketIconTimer.Stop();
            _ticketIconIndex = 2;
            ticketsTab.Icon = _ticketIcons[_ticketIconIndex];
        }
    }

    private void RotateTicketIcon()
    {
        var ticketsTab = this.FindByName<ShellContent>("TicketsTab");
        if (ticketsTab == null) return;

        if (Shell.Current?.CurrentState?.Location?.ToString().Contains("tickets", StringComparison.OrdinalIgnoreCase) != true)
            return;

        _ticketIconIndex = (_ticketIconIndex + 1) % _ticketIcons.Length;
        ticketsTab.Icon = _ticketIcons[_ticketIconIndex];
    }
}
