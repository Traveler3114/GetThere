using System;
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
    private readonly JourneyService _journeyService;
    private readonly TicketCaptureService _capture;
    private readonly TicketStore _store;
    private readonly PendingImportQueue _pendingImports;
    private readonly ImportSyncService _importSync;
    private readonly IAnalyticsService _analytics;
    private ImportedTicketStatus? _activeFilter;
    private ImportSource? _sourceFilter;

    [ObservableProperty]
    private bool _hasImportedTickets;

    /// <summary>Empty means "All"; otherwise an <see cref="ImportedTicketStatus"/> name. Drives chip selection.</summary>
    [ObservableProperty]
    private string _activeFilterKey = string.Empty;

    [ObservableProperty]
    private string _sourceFilterKey = string.Empty;

    [ObservableProperty]
    private string _groupedFilterKey = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private TicketSortOption _selectedSort = TicketSortOption.ValidFromDesc;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private int _totalTickets;

    [ObservableProperty]
    private bool _hasMore;

    /// <summary>True when the list on screen came from the device rather than the server.</summary>
    [ObservableProperty]
    private bool _isShowingCached;

    /// <summary>How old that copy is, in words. Empty unless <see cref="IsShowingCached"/>.</summary>
    [ObservableProperty]
    private string _cachedAtText = string.Empty;

    [ObservableProperty]
    private bool _isSelectionMode;

    [ObservableProperty]
    private int _selectedCount;

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

    /// <summary>Filtered view count for the summary bar — live after search/filters.</summary>
    [ObservableProperty]
    private int _filteredCount;

    [ObservableProperty]
    private int _activeCount;

    public bool HasSelection => SelectedCount > 0;
    public bool ShowSelectionBar => IsSelectionMode && HasSelection;
    public string SelectionSummary => string.Format(LocalizationService.Instance["Tickets_SelectedCount"], SelectedCount);
    public string SortLabel => SelectedSort switch
    {
        TicketSortOption.ValidFromDesc => LocalizationService.Instance["Tickets_SortValidFromDesc"],
        TicketSortOption.ValidFromAsc => LocalizationService.Instance["Tickets_SortValidFromAsc"],
        TicketSortOption.CreatedDesc => LocalizationService.Instance["Tickets_SortCreatedDesc"],
        TicketSortOption.CreatedAsc => LocalizationService.Instance["Tickets_SortCreatedAsc"],
        TicketSortOption.PriceDesc => LocalizationService.Instance["Tickets_SortPriceDesc"],
        TicketSortOption.PriceAsc => LocalizationService.Instance["Tickets_SortPriceAsc"],
        TicketSortOption.NameAsc => LocalizationService.Instance["Tickets_SortNameAsc"],
        TicketSortOption.NameDesc => LocalizationService.Instance["Tickets_SortNameDesc"],
        TicketSortOption.OperatorAsc => LocalizationService.Instance["Tickets_SortOperatorAsc"],
        TicketSortOption.OperatorDesc => LocalizationService.Instance["Tickets_SortOperatorDesc"],
        _ => LocalizationService.Instance["Tickets_SortValidFromDesc"]
    };

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

    partial void OnSearchTextChanged(string value) => RebuildWallet();

    partial void OnGroupedFilterKeyChanged(string value) => RebuildWallet();
    partial void OnSourceFilterKeyChanged(string value) => RebuildWallet();

    partial void OnSelectedSortChanged(TicketSortOption value)
    {
        OnPropertyChanged(nameof(SortLabel));
        RebuildWallet();
    }

    partial void OnIsSelectionModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSelectionBar));
        if (!value)
            ClearSelectionInternal();
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowSelectionBar));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    public TicketsViewModel(
        AuthService authService,
        ImportedTicketService importedService,
        TicketService ticketService,
        JourneyService journeyService,
        TicketCaptureService capture,
        TicketStore store,
        PendingImportQueue pendingImports,
        ImportSyncService importSync,
        IAnalyticsService analytics,
        JourneysViewModel journeys)
    {
        _authService = authService;
        _importedService = importedService;
        _ticketService = ticketService;
        _journeyService = journeyService;
        _capture = capture;
        _store = store;
        _pendingImports = pendingImports;
        _importSync = importSync;
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
        IsShowingCached = false;
        CachedAtText = string.Empty;
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
                    await ShowCachedAsync();
                    return;
                }
            }

            IsAuthenticated = loggedIn;

            // A guest is signed out but may still hold tickets — they are on the device, waiting for
            // an account. Show those rather than the "account required" wall.
            if (!loggedIn)
            {
                await ShowPendingOnlyAsync();
                return;
            }

            // Anything imported offline or before signing in is pushed first, so the list below
            // already contains it rather than showing it twice from two sources.
            await _importSync.FlushAsync();

            var serverSort = ToServerSort(SelectedSort);
            // Use search/source when user has typed/selected them — server does the heavy lifting for
            // imported tickets, client filters the purchased half and refines the merged list.
            var searchForServer = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
            bool? ungrouped = GroupedFilterKey == "Ungrouped" ? true : null;
            bool? hasJourney = GroupedFilterKey == "Grouped" ? true : null;

            var result = await _importedService.ListAsync(page: _currentPage, perPage: PageSize, status: _activeFilter, source: _sourceFilter, sort: serverSort, search: searchForServer, ungrouped: ungrouped, hasJourney: hasJourney);
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

                // Caching is a by-product of a read that already succeeded, not a sync step of its
                // own. Only the unfiltered first page is worth keeping — a cache of "the Used ones"
                // would be a confusing thing to show someone offline.
                if (_activeFilter is null && _sourceFilter is null && string.IsNullOrWhiteSpace(SearchText) && string.IsNullOrWhiteSpace(GroupedFilterKey) && _currentPage == 1)
                    await CacheCurrentAsync();

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
                await ShowCachedAsync();
            }
        }
        catch (Exception ex)
        {
            IsOffline = IsOfflineNow;
            ErrorText = IsOffline ? LocalizationService.Instance["Common_Offline"] : ex.Message;
            HasError = true;
            await ShowCachedAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// The wallet for someone with no account: whatever they have imported, held on the device.
    /// <para>
    /// A guest used to get a full-screen "account required" scrim over an empty tab. Importing works
    /// without an account now, so there is something real to show — and hiding it behind a sign-up
    /// prompt would mean taking a ticket away from the person who just added it.
    /// </para>
    /// </summary>
    private async Task ShowPendingOnlyAsync()
    {
        var pending = await _pendingImports.PeekAllAsync();

        ImportedTickets.Clear();
        _purchasedTickets.Clear();

        // Authenticated in the sense the screen cares about: there is a wallet worth rendering.
        // Nothing here is a claim about the server, and no request will be made with it.
        IsAuthenticated = pending.Count > 0;

        WalletTickets.Clear();
        foreach (var t in pending.Select(WalletTicket.FromPending).OrderByDescending(t => t.SortDate))
            WalletTickets.Add(t);

        HasImportedTickets = WalletTickets.Count > 0;
        TotalTickets = WalletTickets.Count;
        FilteredCount = WalletTickets.Count;
        ActiveCount = WalletTickets.Count(t => t.DisplayStatus == nameof(ImportedTicketStatus.Active));
        HasMore = false;

        if (WalletTickets.Count > 0)
        {
            IsShowingCached = true;
            CachedAtText = LocalizationService.Instance["Tickets_OnThisDeviceOnly"];
        }
    }

    /// <summary>Writes what is currently on screen to the device, so the next failed load has it.</summary>
    private async Task CacheCurrentAsync()
    {
        var owner = await _authService.GetOwnerKeyAsync();
        await _store.SaveImportedAsync(owner, ImportedTickets);
        await _store.SavePurchasedAsync(owner, _purchasedTickets);
    }

    /// <summary>
    /// Falls back to the device's copy after a failed load.
    /// <para>
    /// Only ever reached from a failure path, so it can never mask a live read. It leaves the error
    /// banner in place — the list is real but may be out of date, and
    /// <see cref="CachedAtText"/> says how far.
    /// </para>
    /// </summary>
    private async Task ShowCachedAsync()
    {
        try
        {
            var owner = await _authService.GetOwnerKeyAsync();
            var imported = await _store.ReadImportedAsync(owner);
            var purchased = await _store.ReadPurchasedAsync(owner);

            if (imported is null && purchased is null) return;

            ImportedTickets.Clear();
            foreach (var t in imported?.Items ?? [])
                ImportedTickets.Add(t);

            _purchasedTickets.Clear();
            _purchasedTickets.AddRange(purchased?.Items ?? []);

            RebuildWallet();

            var cachedAt = imported?.CachedAtUtc ?? purchased?.CachedAtUtc;
            IsShowingCached = WalletTickets.Count > 0;
            CachedAtText = cachedAt is { } at
                ? string.Format(LocalizationService.Instance["Tickets_SavedAgo"], DescribeAge(DateTime.UtcNow - at))
                : string.Empty;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[TicketsViewModel] Could not read the cached wallet: {ex.Message}");
        }
    }

    /// <summary>
    /// Coarse, human age of the cache. Deliberately vague — "3 days ago" is what a traveller needs
    /// to judge whether to trust the screen, and a precise timestamp would imply a precision the
    /// underlying status does not have.
    /// </summary>
    private static string DescribeAge(TimeSpan age) => age switch
    {
        { TotalMinutes: < 2 } => LocalizationService.Instance["Tickets_JustNow"],
        { TotalHours: < 1 } => $"{(int)age.TotalMinutes} min",
        { TotalDays: < 1 } => $"{(int)age.TotalHours} h",
        _ => $"{(int)age.TotalDays} d"
    };

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

    private static string? ToServerSort(TicketSortOption option) => option switch
    {
        TicketSortOption.CreatedDesc => "-createdAt",
        TicketSortOption.CreatedAsc => "createdAt",
        TicketSortOption.ValidFromDesc => "-validFrom",
        TicketSortOption.ValidFromAsc => "validFrom",
        TicketSortOption.PriceDesc => "-price",
        TicketSortOption.PriceAsc => "price",
        TicketSortOption.NameAsc => "ticketName",
        TicketSortOption.NameDesc => "-ticketName",
        TicketSortOption.OperatorAsc => "operator",
        TicketSortOption.OperatorDesc => "-operator",
        _ => "-validFrom"
    };

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

        // Source filter: only imported tickets carry a source; purchased are hidden when a source is chosen.
        var sourceFilteredImported = ImportedTickets.AsEnumerable();
        if (_sourceFilter.HasValue)
        {
            sourceFilteredImported = sourceFilteredImported.Where(t => t.Source == _sourceFilter.Value);
            purchased = []; // no source concept
        }

        var merged = sourceFilteredImported.Select(WalletTicket.FromImported)
            .Concat(purchased.Select(WalletTicket.FromPurchased));

        // Grouped filter
        if (GroupedFilterKey == "Ungrouped")
            merged = merged.Where(t => !t.IsGrouped);
        else if (GroupedFilterKey == "Grouped")
            merged = merged.Where(t => t.IsGrouped);

        // Search (client-side refinement — server already filtered imported when search was passed)
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            merged = merged.Where(t =>
                (t.TicketName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.RouteDescription?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.OriginName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.DestinationName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.OperatorNameSnapshot?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Sorting
        merged = SelectedSort switch
        {
            TicketSortOption.ValidFromAsc => merged.OrderBy(t => t.ValidFrom ?? DateTime.MaxValue).ThenBy(t => t.SortDate),
            TicketSortOption.ValidFromDesc => merged.OrderByDescending(t => t.ValidFrom ?? DateTime.MinValue).ThenByDescending(t => t.SortDate),
            TicketSortOption.CreatedAsc => merged.OrderBy(t => t.SortDate),
            TicketSortOption.CreatedDesc => merged.OrderByDescending(t => t.SortDate),
            TicketSortOption.PriceAsc => merged.OrderBy(t => t.Price ?? decimal.MinValue),
            TicketSortOption.PriceDesc => merged.OrderByDescending(t => t.Price ?? decimal.MinValue),
            TicketSortOption.NameAsc => merged.OrderBy(t => t.TicketName ?? string.Empty),
            TicketSortOption.NameDesc => merged.OrderByDescending(t => t.TicketName ?? string.Empty),
            TicketSortOption.OperatorAsc => merged.OrderBy(t => t.OperatorNameSnapshot ?? string.Empty),
            TicketSortOption.OperatorDesc => merged.OrderByDescending(t => t.OperatorNameSnapshot ?? string.Empty),
            _ => merged.OrderByDescending(t => t.SortDate)
        };

        var list = merged.ToList();

        // Preserve selection across rebuilds by id+kind
        var prevSelected = WalletTickets.Where(t => t.IsSelected).Select(t => (t.Kind, t.Id)).ToHashSet();
        WalletTickets.Clear();
        foreach (var t in list)
        {
            if (prevSelected.Contains((t.Kind, t.Id)))
                t.IsSelected = true;
            WalletTickets.Add(t);
        }

        HasImportedTickets = WalletTickets.Count > 0;
        FilteredCount = list.Count;
        // Active count unfiltered for the summary badge
        var activeWanted = nameof(ImportedTicketStatus.Active);
        ActiveCount = ImportedTickets.Count(t => t.Status.ToString() == activeWanted) + _purchasedTickets.Count(t => t.Status.ToString() == activeWanted);
        SelectedCount = WalletTickets.Count(t => t.IsSelected);
    }

    /// <summary>
    /// Opens a ticket. Tapping a card used to offer an action sheet; that is a secondary control
    /// now, because the first thing a traveller wants from a wallet is the ticket itself.
    /// </summary>
    [RelayCommand]
    private async Task OpenTicket(WalletTicket? ticket)
    {
        if (ticket is null) return;
        if (IsSelectionMode)
        {
            ToggleSelectTicket(ticket);
            return;
        }

        // Not yet pushed, so it has no server id and both detail screens fetch by one. Say so rather
        // than opening a screen that would fail to load.
        if (ticket.IsPending)
        {
            ErrorText = LocalizationService.Instance["Tickets_PendingNotOpenable"];
            HasError = true;
            return;
        }

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
            var serverSort = ToServerSort(SelectedSort);
            var searchForServer = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
            bool? ungrouped = GroupedFilterKey == "Ungrouped" ? true : null;
            bool? hasJourney = GroupedFilterKey == "Grouped" ? true : null;
            var result = await _importedService.ListAsync(page: _currentPage, perPage: PageSize, status: _activeFilter, source: _sourceFilter, sort: serverSort, search: searchForServer, ungrouped: ungrouped, hasJourney: hasJourney);
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

    [RelayCommand]
    private async Task FilterSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            _sourceFilter = null;
            SourceFilterKey = string.Empty;
        }
        else if (Enum.TryParse<ImportSource>(source, out var parsed))
        {
            _sourceFilter = parsed;
            SourceFilterKey = parsed.ToString();
        }
        else
        {
            return;
        }
        await LoadTickets();
    }

    [RelayCommand]
    private void FilterGrouped(string? key)
    {
        GroupedFilterKey = key ?? string.Empty;
        // client-only, no reload needed beyond rebuild (but reload to respect server ungrouped)
        if (!string.IsNullOrWhiteSpace(SearchText) || _sourceFilter is not null)
            _ = LoadTickets();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        _ = LoadTickets();
    }

    [RelayCommand]
    private async Task PickSort()
    {
        if (Shell.Current is null) return;
        var options = new[]
        {
            (LocalizationService.Instance["Tickets_SortValidFromDesc"], TicketSortOption.ValidFromDesc),
            (LocalizationService.Instance["Tickets_SortValidFromAsc"], TicketSortOption.ValidFromAsc),
            (LocalizationService.Instance["Tickets_SortCreatedDesc"], TicketSortOption.CreatedDesc),
            (LocalizationService.Instance["Tickets_SortCreatedAsc"], TicketSortOption.CreatedAsc),
            (LocalizationService.Instance["Tickets_SortPriceDesc"], TicketSortOption.PriceDesc),
            (LocalizationService.Instance["Tickets_SortPriceAsc"], TicketSortOption.PriceAsc),
            (LocalizationService.Instance["Tickets_SortNameAsc"], TicketSortOption.NameAsc),
            (LocalizationService.Instance["Tickets_SortNameDesc"], TicketSortOption.NameDesc),
            (LocalizationService.Instance["Tickets_SortOperatorAsc"], TicketSortOption.OperatorAsc),
            (LocalizationService.Instance["Tickets_SortOperatorDesc"], TicketSortOption.OperatorDesc),
        };
        var labels = options.Select(o => o.Item1).ToArray();
        var choice = await Shell.Current.DisplayActionSheetAsync(LocalizationService.Instance["Tickets_SortTitle"], LocalizationService.Instance["App_Cancel"], null, labels);
        var matched = options.FirstOrDefault(o => o.Item1 == choice);
        if (matched != default)
            SelectedSort = matched.Item2;
    }

    // ── Selection / grouping ───────────────────────────────────────────────

    [RelayCommand]
    private void ToggleSelectionMode()
    {
        IsSelectionMode = !IsSelectionMode;
        if (!IsSelectionMode)
            ClearSelectionInternal();
    }

    [RelayCommand]
    private void ToggleSelectTicket(WalletTicket? ticket)
    {
        if (ticket is null) return;
        ticket.IsSelected = !ticket.IsSelected;
        SelectedCount = WalletTickets.Count(t => t.IsSelected);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var t in WalletTickets) t.IsSelected = true;
        SelectedCount = WalletTickets.Count;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        ClearSelectionInternal();
    }

    private void ClearSelectionInternal()
    {
        foreach (var t in WalletTickets) t.IsSelected = false;
        SelectedCount = 0;
    }

    private (List<int> importedIds, List<int> ticketIds) CollectSelection()
    {
        var imported = WalletTickets.Where(t => t.IsSelected && t.Kind == WalletTicketKind.Imported).Select(t => t.Id).ToList();
        var purchased = WalletTickets.Where(t => t.IsSelected && t.Kind == WalletTicketKind.Purchased).Select(t => t.Id).ToList();
        return (imported, purchased);
    }

    [RelayCommand]
    private async Task CreateJourneyFromSelection()
    {
        var (importedIds, ticketIds) = CollectSelection();
        var total = importedIds.Count + ticketIds.Count;
        if (total == 0)
        {
            ErrorText = LocalizationService.Instance["Tickets_SelectAtLeastOne"];
            HasError = true;
            return;
        }
        if (Shell.Current is null) return;
        var name = await Shell.Current.DisplayPromptAsync(
            LocalizationService.Instance["Journeys_NewTitle"],
            LocalizationService.Instance["Tickets_NewJourneyPrompt"],
            accept: LocalizationService.Instance["Common_Create"],
            cancel: LocalizationService.Instance["App_Cancel"],
            placeholder: LocalizationService.Instance["Journeys_NewPrompt"],
            maxLength: 200);
        if (string.IsNullOrWhiteSpace(name)) return;

        IsBusy = true;
        HasError = false;
        try
        {
            var result = await _journeyService.CreateAsync(new CreateJourneyRequest
            {
                Name = name.Trim(),
                ImportedTicketIds = importedIds,
                TicketIds = ticketIds
            });
            if (!result.Success)
            {
                ErrorText = result.Message ?? LocalizationService.Instance["Journeys_CouldNotCreate"];
                HasError = true;
                return;
            }
            _analytics.TrackEvent("journey_created", new() { ["source"] = "wallet_selection", ["legs"] = total.ToString() });
            ClearSelectionInternal();
            IsSelectionMode = false;
            await Journeys.LoadCommand.ExecuteAsync(null);
            ShowJourneys = true;
            await Shell.Current.GoToAsync($"journeydetail?journeyId={result.Data!.Id}");
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            HasError = true;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task AddSelectionToJourney()
    {
        var (importedIds, ticketIds) = CollectSelection();
        var total = importedIds.Count + ticketIds.Count;
        if (total == 0)
        {
            ErrorText = LocalizationService.Instance["Tickets_SelectAtLeastOne"];
            HasError = true;
            return;
        }
        if (Shell.Current is null) return;

        // The Journeys segment lazy-loads, so it may still be empty here even when the user has
        // journeys — load it before deciding there are none to add to.
        if (!Journeys.HasLoadedOnce)
            await Journeys.LoadCommand.ExecuteAsync(null);

        if (Journeys.Journeys.Count == 0)
        {
            ErrorText = LocalizationService.Instance["Tickets_NoJourneysToAdd"];
            HasError = true;
            return;
        }

        // Pick the target journey. The action sheet scrolls, so it handles any number of journeys;
        // the picker page is the other direction (tickets for a known journey) and can't choose one.
        var labels = Journeys.Journeys.Select(j => j.Name).ToArray();
        var pick = await Shell.Current.DisplayActionSheetAsync(LocalizationService.Instance["Tickets_AddToJourneyTitle"], LocalizationService.Instance["App_Cancel"], null, labels);
        if (string.IsNullOrWhiteSpace(pick) || pick == LocalizationService.Instance["App_Cancel"]) return;
        var chosen = Journeys.Journeys.FirstOrDefault(j => j.Name == pick);

        if (chosen is null) return;
        IsBusy = true;
        HasError = false;
        try
        {
            var result = await _journeyService.AddTicketsAsync(chosen.Id, new JourneyMembershipRequest { ImportedTicketIds = importedIds, TicketIds = ticketIds });
            if (!result.Success)
            {
                ErrorText = result.Message ?? LocalizationService.Instance["Journeys_CouldNotCreate"];
                HasError = true;
                return;
            }
            _analytics.TrackEvent("journey_tickets_added", new() { ["count"] = total.ToString(), ["journeyId"] = chosen.Id.ToString() });
            ClearSelectionInternal();
            IsSelectionMode = false;
            await Journeys.LoadCommand.ExecuteAsync(null);
            await LoadTickets();
            await Shell.Current.GoToAsync($"journeydetail?journeyId={chosen.Id}");
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            HasError = true;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void EnterGroupingFromJourneys()
    {
        ShowJourneys = false;
        IsSelectionMode = true;
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

        var options = new List<string> { "Mark as used", "Cancel ticket", "Select for journey" };
        var choice = await Shell.Current.DisplayActionSheetAsync(
            ticket.TicketName ?? "Ticket", "Close", null, options.ToArray());

        switch (choice)
        {
            case "Mark as used":
                await MarkUsed(ticket);
                break;
            case "Cancel ticket":
                await CancelTicket(ticket);
                break;
            case "Select for journey":
                IsSelectionMode = true;
                walletTicket.IsSelected = true;
                SelectedCount = WalletTickets.Count(t => t.IsSelected);
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
