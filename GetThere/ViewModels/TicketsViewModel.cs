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
    private readonly IAnalyticsService _analytics;

    [ObservableProperty]
    private bool _isGuest;

    [ObservableProperty]
    private bool _hasImportedTickets;

    [ObservableProperty]
    private string _filterLabel = string.Empty;

    private ImportedTicketStatus? _activeFilter;

    public ObservableCollection<ImportedTicketResponse> ImportedTickets { get; } = [];

    public TicketsViewModel(AuthService authService, ImportedTicketService importedService, IAnalyticsService analytics)
    {
        _authService = authService;
        _importedService = importedService;
        _analytics = analytics;
    }

    [RelayCommand]
    private async Task LoadTickets()
    {
        IsGuest = string.IsNullOrWhiteSpace(await _authService.GetTokenAsync()) || AuthService.IsGuest();
        if (IsGuest) return;

        IsBusy = true;

        try
        {
            var result = await _importedService.ListAsync();
            ImportedTickets.Clear();
            if (result.Success && result.Data is not null)
            {
                var filtered = _activeFilter.HasValue
                    ? result.Data.Where(t => t.Status == _activeFilter.Value)
                    : result.Data;

                foreach (var t in filtered)
                    ImportedTickets.Add(t);
            }

            HasImportedTickets = ImportedTickets.Count > 0;
            UpdateFilterLabel();
            _analytics.TrackEvent("tickets_loaded", new() { ["count"] = ImportedTickets.Count.ToString() });
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SetFilter(string? status)
    {
        _activeFilter = status is not null && Enum.TryParse<ImportedTicketStatus>(status, out var parsed) ? parsed : null;
        _ = LoadTickets();
    }

    [RelayCommand]
    private async Task ImportTicket()
    {
        await Shell.Current.GoToAsync("importticket");
    }

    [RelayCommand]
    private async Task CancelTicket(ImportedTicketResponse ticket)
    {
        if (ticket is null) return;
        var result = await _importedService.CancelAsync(ticket.Id);
        if (result.Success)
            await LoadTickets();
    }

    [RelayCommand]
    private async Task GoToLogin()
    {
        App.GoToLogin();
    }

    private void UpdateFilterLabel()
    {
        FilterLabel = _activeFilter switch
        {
            ImportedTicketStatus.Active => "Active",
            ImportedTicketStatus.Used => "Used",
            ImportedTicketStatus.Expired => "Expired",
            ImportedTicketStatus.Cancelled => "Cancelled",
            _ => "All"
        };
    }
}
