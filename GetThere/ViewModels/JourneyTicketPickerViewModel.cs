using System.Collections.ObjectModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;
using GetThere.Services;

using GetThereShared.Contracts;

namespace GetThere.ViewModels;

[QueryProperty(nameof(JourneyId), "journeyId")]
public partial class JourneyTicketPickerViewModel : BaseViewModel
{
    private readonly ImportedTicketService _importedService;
    private readonly TicketService _ticketService;
    private readonly JourneyService _journeyService;
    private readonly IAnalyticsService _analytics;

    private string _journeyId = string.Empty;
    private int _journeyIdParsed;

    public string JourneyId
    {
        get => _journeyId;
        set
        {
            _journeyId = value;
            if (int.TryParse(value, out var parsed))
            {
                _journeyIdParsed = parsed;
                _ = LoadAsync();
            }
        }
    }

    [ObservableProperty]
    private string _journeyName = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private int _selectedCount;

    public ObservableCollection<WalletTicket> Tickets { get; } = [];
    private readonly List<WalletTicket> _allTickets = [];

    public bool HasSelection => SelectedCount > 0;
    public bool HasTickets => Tickets.Count > 0;
    public string SelectionSummary => HasSelection
        ? string.Format(LocalizationService.Instance["Tickets_SelectedCount"], SelectedCount)
        : LocalizationService.Instance["JourneyPicker_SelectHint"];

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public JourneyTicketPickerViewModel(ImportedTicketService importedService, TicketService ticketService, JourneyService journeyService, IAnalyticsService analytics)
    {
        _importedService = importedService;
        _ticketService = ticketService;
        _journeyService = journeyService;
        _analytics = analytics;
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        HasError = false;
        try
        {
            // Try to get journey name for header
            var journey = await _journeyService.GetByIdAsync(_journeyIdParsed);
            if (journey.Success && journey.Data is not null)
                JourneyName = journey.Data.Name;

            // Load ungrouped imported tickets from server, and purchased tickets client-side
            var importedResult = await _importedService.ListAsync(page: 1, perPage: 100, ungrouped: true);
            var imported = importedResult.Success ? importedResult.Data!.Data : [];

            List<TicketResponse> purchased = [];
            try
            {
                var purchasedResult = await _ticketService.GetMyTicketsAsync();
                if (purchasedResult.Success && purchasedResult.Data is not null)
                    purchased = purchasedResult.Data.Where(t => t.JourneyId == null).ToList();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[JourneyTicketPicker] Purchased load failed: {ex.Message}");
            }

            _allTickets.Clear();
            foreach (var t in imported.Select(WalletTicket.FromImported))
                _allTickets.Add(t);
            foreach (var t in purchased.Select(WalletTicket.FromPurchased))
                _allTickets.Add(t);

            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            HasError = true;
        }
        finally { IsBusy = false; }
    }

    private void ApplyFilter()
    {
        IEnumerable<WalletTicket> query = _allTickets;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim().ToLowerInvariant();
            query = query.Where(t =>
                (t.TicketName?.ToLowerInvariant().Contains(term) ?? false) ||
                (t.RouteDescription?.ToLowerInvariant().Contains(term) ?? false) ||
                (t.OriginName?.ToLowerInvariant().Contains(term) ?? false) ||
                (t.DestinationName?.ToLowerInvariant().Contains(term) ?? false) ||
                (t.OperatorNameSnapshot?.ToLowerInvariant().Contains(term) ?? false));
        }

        // Keep sort stable: by ValidFrom desc, then name — matches wallet default
        query = query.OrderByDescending(t => t.ValidFrom ?? DateTime.MinValue).ThenBy(t => t.TicketName);

        Tickets.Clear();
        foreach (var t in query)
            Tickets.Add(t);

        OnPropertyChanged(nameof(HasTickets));
        SelectedCount = Tickets.Count(t => t.IsSelected);
    }

    [RelayCommand]
    private void ToggleSelect(WalletTicket? ticket)
    {
        if (ticket is null) return;
        ticket.IsSelected = !ticket.IsSelected;
        SelectedCount = Tickets.Count(t => t.IsSelected) + _allTickets.Except(Tickets).Count(t => t.IsSelected);
        // Re-evaluate filtered count difference — hidden items that match search still count if selected
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var t in _allTickets) t.IsSelected = true;
        // reflect in visible collection (same objects)
        SelectedCount = _allTickets.Count(t => t.IsSelected);
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var t in _allTickets) t.IsSelected = false;
        SelectedCount = 0;
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private async Task Confirm()
    {
        var importedIds = _allTickets.Where(t => t.IsSelected && t.Kind == WalletTicketKind.Imported).Select(t => t.Id).ToList();
        var ticketIds = _allTickets.Where(t => t.IsSelected && t.Kind == WalletTicketKind.Purchased).Select(t => t.Id).ToList();
        var total = importedIds.Count + ticketIds.Count;
        if (total == 0)
        {
            ErrorText = LocalizationService.Instance["Tickets_SelectAtLeastOne"];
            HasError = true;
            return;
        }

        IsBusy = true;
        HasError = false;
        try
        {
            var result = await _journeyService.AddTicketsAsync(_journeyIdParsed, new JourneyMembershipRequest
            {
                ImportedTicketIds = importedIds,
                TicketIds = ticketIds
            });
            if (!result.Success)
            {
                ErrorText = result.Message ?? LocalizationService.Instance["Journeys_CouldNotCreate"];
                HasError = true;
                return;
            }

            _analytics.TrackEvent("journey_tickets_added", new() { ["count"] = total.ToString(), ["journeyId"] = _journeyIdParsed.ToString() });
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            HasError = true;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");
}
