using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Services;

using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThere.ViewModels;

public partial class TicketsViewModel : BaseViewModel
{
    private readonly AuthService _authService;
    private readonly ImportedTicketService _importedService;
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

    public ObservableCollection<ImportedTicketResponse> ImportedTickets { get; } = [];

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
        TicketCaptureService capture,
        IAnalyticsService analytics,
        JourneysViewModel journeys)
    {
        _authService = authService;
        _importedService = importedService;
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
        var loggedIn = await _authService.IsLoggedInAsync();
        IsAuthenticated = loggedIn;
        if (!loggedIn) return;

        IsBusy = true;
        HasError = false;
        _currentPage = 1;
        try
        {
            var result = await _importedService.ListAsync(page: _currentPage, perPage: PageSize, status: _activeFilter);
            if (result.Success)
            {
                var paged = result.Data!;
                ImportedTickets.Clear();
                foreach (var t in paged.Data)
                    ImportedTickets.Add(t);
                TotalTickets = paged.Total;
                HasImportedTickets = ImportedTickets.Count > 0;
                HasMore = _currentPage < paged.TotalPages;
                _analytics.TrackEvent("tickets_loaded", new() { ["count"] = ImportedTickets.Count.ToString() });
            }
            else
            {
                ErrorText = result.Message ?? "Could not load tickets.";
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            HasError = true;
        }
        finally { IsBusy = false; }
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
        var text = await Shell.Current.DisplayPromptAsync(
            "Paste booking text",
            "Paste the confirmation email or booking details.",
            accept: "Read it", cancel: "Cancel",
            placeholder: "Paste here", maxLength: 20000);

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
    private async Task ShowTicketActions(ImportedTicketResponse ticket)
    {
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
