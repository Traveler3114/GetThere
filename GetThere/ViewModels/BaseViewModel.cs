using CommunityToolkit.Mvvm.ComponentModel;

namespace GetThere.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isAuthenticated;

    /// <summary>
    /// Set when a load failed because the device has no connection, as opposed to the server
    /// answering with an error. The distinction matters on screens that gate on being signed in: a
    /// refresh that could not reach the server is not a signed-out user, and treating it as one
    /// shows an "account required" prompt to someone who is perfectly well signed in.
    /// </summary>
    [ObservableProperty]
    private bool _isOffline;

    /// <summary>
    /// Whether the device currently reports no route to the internet.
    /// <para>
    /// Advisory only — connectivity APIs lie in both directions (a captive portal reports Internet,
    /// a VPN handover can report None for a moment). Use it to choose what to *say* about a failure
    /// that already happened, never to decide whether to attempt a request.
    /// </para>
    /// </summary>
    protected static bool IsOfflineNow =>
        Connectivity.Current.NetworkAccess != NetworkAccess.Internet;
}
