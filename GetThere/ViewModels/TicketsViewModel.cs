using System.Collections.ObjectModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;
using GetThere.Services;

using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThere.ViewModels;

public partial class TicketsViewModel : BaseViewModel
{
    private readonly AuthService _authService;
    private readonly ImportedTicketService _importedService;
    private readonly TicketService _ticketService;
    private readonly TicketCaptureService _capture;
    private readonly IAnalyticsService _analytics;
    private ImportedTicketStatus? _activeFilter;

    [ObservableProperty]
    private bool _hasImportedTickets;

    /// <summary>Empty means "All"; otherwise an <see cref="ImportedTicketStatus"/> name. Drives chip selection.</summary>
    [ObservableProperty]
    private string _activeFilterKey = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private int _totalTickets;

    [ObservableProperty]
    private bool _hasMore;

    private int _currentPage = 1;
    private const int PageSize = 50;

    /// <summary>Imported tickets as the server returned them, kept for paging and the card actions.</summary>
    public ObservableCollection<ImportedTicketResponse> ImportedTickets { get; } = [];

    /// <summary>Purchased tickets. Not paged — <c>GET /tickets</c> returns the whole history.</summary>
    private readonly List<TicketResponse> _purchasedTickets = [];

    /// <summary>
    /// What the wallet actually lists: both kinds in one place, newest first.
    /// <para>
    /// Until this existed the screen showed imported tickets only, so a ticket the user had *paid
    /// for* was visible for a few seconds after purchase and then unreachable.
    /// </para>
    /// </summary>
    public ObservableCollection<WalletTicket> WalletTickets { get; } = [];

    /// <summary>
    /// The Journeys half of this screen. Composed rather than merged: journeys and tickets are two
    /// views of the same wallet behind one segmented control, but their loads, busy flags and error
    /// states are independent — a failing journeys call must not blank the ticket list.
    /// </summary>
    public JourneysViewModel Journeys { get; }

    /// <summary>Which half of the segmented control is showing. False = Tickets.</summary>
    [ObservableProperty]
    private bool _showJourneys;

    /// <summary>Inverse of <see cref="ShowJourneys"/>, so the Tickets side can bind without a converter.</summary>
    public bool ShowTickets => !ShowJourneys;

    /// <summary>Drives chip styling on the segmented control, which compares against a key string.</summary>
    public string SegmentKey => ShowJourneys ? "Journeys" : "Tickets";

    partial void OnShowJourneysChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowTickets));
        OnPropertyChanged(nameof(SegmentKey));
    }

    public TicketsViewModel(
        AuthService authService,
        ImportedTicketService importedService,
        TicketService ticketService,
        TicketCaptureService capture,
        IAnalyticsService analytics,
        JourneysViewModel journeys)
    {
        _authService = authService;
        _importedService = importedService;
        _ticketService = ticketService;
        _capture = capture;
        _analytics = analytics;
        Journeys = journeys;
    }

    /// <summary>
    /// Switches between the two halves, loading journeys the first time they are shown rather than
    /// on every page appearance — most sessions never open the tab.
    /// </summary>
    [RelayCommand]
    private async Task ShowSegment(string? segment)
    {
        var journeys = string.Equals(segment, "Journeys", StringComparison.OrdinalIgnoreCase);
        if (journeys == ShowJourneys) return;

        ShowJourneys = journeys;

        if (journeys && !Journeys.HasLoadedOnce)
            await Journeys.LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task LoadTickets()
    {
        IsBusy = true;
        HasError = false;
        IsOffline = false;
        _currentPage = 1;
        try
        {
            // Inside the try deliberately. This reaches the network — an expired access token sends
            // IsLoggedInAsync straight into a refresh — and it used to sit above it, so the first
            // thing this screen did without a connection was throw past every handler below.
            var loggedIn = await _authService.IsLoggedInAsync();

            if (!loggedIn && IsOfflineNow)
            {
                // A refresh that could not reach the server says nothing about whether the user is
                // signed in, so fall back to whether credentials are still on file — a SecureStorage
                // read, no network. Without this the wallet raises its full-screen "account
                // required" prompt at someone who is merely offline, and on a cold start
                // IsAuthenticated has never been true, so there is no previous value to keep.
                var hasStoredCredentials = !string.IsNullOrWhiteSpace(await _authService.GetRefreshTokenAsync());
                if (hasStoredCredentials)
                {
                    IsAuthenticated = true;
                    IsOffline = true;
                    HasError = true;
                    ErrorText = LocalizationService.Instance["Common_Offline"];
                    return;
                }
            }

            IsAuthenticated = loggedIn;
            if (!loggedIn) return;

            var result = await _importedService.ListAsync(page: _currentPage, perPage: PageSize, status: _activeFilter);
            if (result.Success)
            {
                var paged = result.Data!;
                ImportedTickets.Clear();
                foreach (var t in paged.Data)
                    ImportedTickets.Add(t);
                TotalTickets = paged.Total;
                HasMore = _currentPage < paged.TotalPages;

                // Purchased tickets are fetched separately and merged below. GET /tickets is
                // unpaged and unfiltered — the whole history in one response — so it cannot join the
                // paging above and is simply re-read whenever the first page is.
                await LoadPurchasedAsync();
                RebuildWallet();

                _analytics.TrackEvent("tickets_loaded", new() { ["count"] = WalletTickets.Count.ToString() });
            }
            else
            {
                // The service turns every transport failure into one generic message, so ask the
                // device rather than the message which kind of failure this was.
                IsOffline = IsOfflineNow;
                ErrorText = IsOffline
                    ? LocalizationService.Instance["Common_Offline"]
                    : result.Message ?? "Could not load tickets.";
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            IsOffline = IsOfflineNow;
            ErrorText = IsOffline ? LocalizationService.Instance["Common_Offline"] : ex.Message;
            HasError = true;
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Reads the purchased half. Deliberately forgiving: a wallet that can show the user's imported
    /// tickets should show them even if the purchased call fails, so a failure here empties that
    /// half rather than blanking the screen or raising the shared error banner.
    /// </summary>
    private async Task LoadPurchasedAsync()
    {
        _purchasedTickets.Clear();
        try
        {
            var result = await _ticketService.GetMyTicketsAsync();
            if (result.Success && result.Data is not null)
                _purchasedTickets.AddRange(result.Data);
            else
                Trace.WriteLine($"[TicketsViewModel] Purchased tickets unavailable: {result.Message}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[TicketsViewModel] Purchased tickets unavailable: {ex.Message}");
        }
    }

    /// <summary>
    /// Projects both sources into one list, newest first.
    /// <para>
    /// The status chips filter imported tickets server-side, so the same filter is applied to the
    /// purchased half here — otherwise selecting "Used" would leave every purchased ticket on
    /// screen and the filter would look broken.
    /// </para>
    /// </summary>
    private void RebuildWallet()
    {
        var purchased = _purchasedTickets.AsEnumerable();

        if (_activeFilter is { } filter)
        {
            // The two enums are separate types that share their names; comparing the names is what
            // lets one chip mean the same thing on both halves.
            var wanted = filter.ToString();
            purchased = purchased.Where(t => string.Equals(t.Status.ToString(), wanted, StringComparison.Ordinal));
        }

        var merged = ImportedTickets.Select(WalletTicket.FromImported)
            .Concat(purchased.Select(WalletTicket.FromPurchased))
            .OrderByDescending(t => t.SortDate)
            .ToList();

        WalletTickets.Clear();
        foreach (var t in merged)
            WalletTickets.Add(t);

        HasImportedTickets = WalletTickets.Count > 0;
    }

    /// <summary>
    /// Opens a ticket. Tapping a card used to offer an action sheet; that is a secondary control
    /// now, because the first thing a traveller wants from a wallet is the ticket itself.
    /// </summary>
    [RelayCommand]
    private async Task OpenTicket(WalletTicket? ticket)
    {
        if (ticket is null) return;

        var route = ticket.Kind switch
        {
            WalletTicketKind.Purchased => $"ticketdetail?ticketId={ticket.Id}",
            _ => $"importedticketdetail?ticketId={ticket.Id}"
        };

        await Shell.Current.GoToAsync(route);
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if (IsBusy || !HasMore) return;
        IsBusy = true;
        HasError = false;
        _currentPage++;
        try
        {
            var result = await _importedService.ListAsync(page: _currentPage, perPage: PageSize, status: _activeFilter);
            if (result.Success)
            {
                var paged = result.Data!;
                foreach (var t in paged.Data)
                    ImportedTickets.Add(t);
                HasMore = _currentPage < paged.TotalPages;

                // Only the imported half pages, so the purchased half is left as it is and the
                // merged list is rebuilt around the newly appended rows.
                RebuildWallet();
            }
            else
            {
                _currentPage--;
                ErrorText = result.Message ?? "Could not load more tickets.";
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            _currentPage--;
            ErrorText = ex.Message;
            HasError = true;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task Filter(string? status)
    {
        _activeFilter = status is not null && Enum.TryParse<ImportedTicketStatus>(status, out var parsed) ? parsed : null;
        ActiveFilterKey = _activeFilter?.ToString() ?? string.Empty;
        await LoadTickets();
    }

    private const string TakePhoto = "Take a photo";
    private const string ChoosePhoto = "Choose from photos";
    private const string ChooseFile = "Choose a file";
    private const string PasteText = "Paste booking text";
    private const string EnterManually = "Enter manually";

    /// <summary>
    /// Asks where the ticket is coming from, then captures it and hands a prefilled draft to the
    /// import form.
    /// <para>
    /// Every branch ends on the same form: it is the one place a ticket is confirmed and validated,
    /// so a scanned pass and a typed ticket take the same path to being saved. Photographing a code
    /// is the scan option — the server decodes QR, Aztec and PDF417 out of the uploaded image, so no
    /// on-device scanner is needed.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task ImportTicket()
    {
        HasError = false;

        var choice = await Shell.Current.DisplayActionSheetAsync(
            "Add a ticket", "Cancel", null,
            TakePhoto, ChoosePhoto, ChooseFile, PasteText, EnterManually);

        // Both the cancel button and a dismissed sheet, which returns null on some platforms.
        if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;

        if (choice == EnterManually)
        {
            _analytics.TrackEvent("ticket_import_started", new() { ["method"] = "manual" });
            await Shell.Current.GoToAsync("importticket");
            return;
        }

        IsBusy = true;
        try
        {
            var draft = choice switch
            {
                TakePhoto => await CaptureAsync(() => _capture.CapturePhotoAsync(), "camera"),
                ChoosePhoto => await CaptureAsync(() => _capture.PickPhotoAsync(), "photo"),
                ChooseFile => await CaptureAsync(() => _capture.PickFileAsync(), "file"),
                PasteText => await PasteAsync(),
                _ => null
            };

            // Null means the user backed out of the picker, which is not an error.
            if (draft is null) return;

            await Shell.Current.GoToAsync("importticket",
                new Dictionary<string, object> { [TicketImportDraft.QueryKey] = draft });
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            HasError = true;
        }
        finally { IsBusy = false; }
    }

    /// <summary>Picks a file, uploads it, and turns what the server read into a draft.</summary>
    private async Task<TicketImportDraft?> CaptureAsync(Func<Task<CapturedTicketFile?>> pick, string method)
    {
        var file = await pick();
        if (file is null) return null;

        _analytics.TrackEvent("ticket_import_started", new() { ["method"] = method });

        using var content = new MemoryStream(file.Content);
        var result = await _importedService.UploadAsync(content, file.FileName, file.ContentType);

        if (!result.Success || result.Data is null)
        {
            ErrorText = result.Message ?? "Could not read that file.";
            HasError = true;
            return null;
        }

        return TicketImportDraft.FromUpload(result.Data);
    }

    /// <summary>
    /// Scrapes a pasted confirmation. Nothing is stored, so this draft carries no blob key.
    /// </summary>
    private async Task<TicketImportDraft?> PasteAsync()
    {
        // Read from the clipboard rather than through DisplayPromptAsync, which renders a
        // single-line Entry: a pasted confirmation email arrived truncated at its first newline, so
        // the scraper only ever saw one line. It reads route, endpoints, price, currency and the
        // validity window out of the full text and could find none of that in a single heading —
        // which is why this path appeared to extract almost nothing.
        //
        // The prompt stays as the fallback for an empty clipboard, so there is still a way to type
        // a line by hand. Either way the user reviews everything on the import form before saving.
        string? text = null;

        try
        {
            if (Clipboard.Default.HasText)
                text = await Clipboard.Default.GetTextAsync();
        }
        catch (Exception ex)
        {
            // Clipboard access can be denied by the platform; fall through to the prompt.
            Trace.WriteLine($"[TicketsViewModel] Could not read the clipboard: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            text = await Shell.Current.DisplayPromptAsync(
                "Paste booking text",
                "Copy the confirmation email first, or type the booking details here.",
                accept: "Read it", cancel: "Cancel",
                placeholder: "Paste here", maxLength: 20000);
        }

        if (string.IsNullOrWhiteSpace(text)) return null;

        _analytics.TrackEvent("ticket_import_started", new() { ["method"] = "text" });

        var result = await _importedService.ExtractTextAsync(text);
        if (!result.Success || result.Data is null)
        {
            ErrorText = result.Message ?? "Could not read that text.";
            HasError = true;
            return null;
        }

        return TicketImportDraft.FromText(result.Data);
    }

    /// <summary>
    /// Offers the actions available on a ticket. Tapping a card used to cancel it outright — the
    /// only interaction the list had, offered even on tickets already used or expired. Cancelling
    /// is now one deliberate choice among several rather than the default consequence of a tap.
    /// </summary>
    [RelayCommand]
    private async Task ShowTicketActions(WalletTicket? walletTicket)
    {
        // Only imported tickets have actions. No API moves a purchased ticket out of Active — the
        // expiry worker is the only thing that ever changes its status — so a menu here would offer
        // buttons that cannot work.
        if (walletTicket?.Imported is not { } ticket) return;

        if (ticket.Status != ImportedTicketStatus.Active)
        {
            // Nothing can be done to a terminal ticket, so offering a menu would only lead to a
            // rejection. Say why instead.
            ErrorText = $"That ticket is {ticket.Status.ToString().ToLowerInvariant()} — no actions are available.";
            HasError = true;
            return;
        }

        var choice = await Shell.Current.DisplayActionSheetAsync(
            ticket.TicketName ?? "Ticket", "Close", null, "Mark as used", "Cancel ticket");

        switch (choice)
        {
            case "Mark as used":
                await MarkUsed(ticket);
                break;
            case "Cancel ticket":
                await CancelTicket(ticket);
                break;
        }
    }

    [RelayCommand]
    private async Task CancelTicket(ImportedTicketResponse ticket)
    {
        // The server rejects cancelling a terminal ticket; catching it here means the user gets
        // told before a round trip rather than after a 400.
        if (ticket.Status != ImportedTicketStatus.Active)
        {
            ErrorText = $"That ticket is already {ticket.Status.ToString().ToLowerInvariant()}.";
            HasError = true;
            return;
        }

        var confirm = await Shell.Current.DisplayAlertAsync("Cancel Ticket", "Cancel this ticket?", "Yes", "No");
        if (!confirm) return;
        var result = await _importedService.CancelAsync(ticket.Id);
        if (result.Success)
        {
            await LoadTickets();
        }
        else
        {
            ErrorText = result.Message ?? "Could not cancel ticket.";
            HasError = true;
        }
    }

    /// <summary>
    /// Marks a ticket used. Nothing in the app could reach this before, so the "Used" filter chip
    /// could only ever show an empty list.
    /// </summary>
    [RelayCommand]
    private async Task MarkUsed(ImportedTicketResponse ticket)
    {
        if (ticket.Status != ImportedTicketStatus.Active)
        {
            ErrorText = $"That ticket is already {ticket.Status.ToString().ToLowerInvariant()}.";
            HasError = true;
            return;
        }

        var result = await _importedService.UpdateStatusAsync(ticket.Id, ImportedTicketStatus.Used);
        if (result.Success)
        {
            _analytics.TrackEvent("ticket_marked_used");
            await LoadTickets();
        }
        else
        {
            ErrorText = result.Message ?? "Could not update ticket.";
            HasError = true;
        }
    }

    [RelayCommand]
    private static async Task GoToLogin()
    {
        App.GoToLogin();
        await Task.CompletedTask;
    }
}
