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
    private ImportedTicketStatus? _activeFilter;

    [ObservableProperty]
    private bool _hasImportedTickets;

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
        var loggedIn = await _authService.IsLoggedInAsync();
        IsAuthenticated = loggedIn;
        if (!loggedIn) return;

        IsBusy = true;
        try
        {
            var result = await _importedService.ListAsync();
            if (result.Success)
            {
                ImportedTickets.Clear();
                var tickets = result.Data!;
                if (_activeFilter.HasValue)
                    tickets = tickets.Where(t => t.Status == _activeFilter.Value).ToList();
                foreach (var t in tickets)
                    ImportedTickets.Add(t);
                HasImportedTickets = ImportedTickets.Count > 0;
                _analytics.TrackEvent("tickets_loaded", new() { ["count"] = ImportedTickets.Count.ToString() });
            }
        }
        catch { }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task Filter(string? status)
    {
        _activeFilter = status is not null && Enum.TryParse<ImportedTicketStatus>(status, out var parsed) ? parsed : null;
        await LoadTickets();
    }

    [RelayCommand]
    private async Task ImportTicket()
    {
        await Shell.Current.GoToAsync("importticket");
    }

    [RelayCommand]
    private async Task CancelTicket(ImportedTicketResponse ticket)
    {
        var confirm = await Shell.Current.DisplayAlert("Cancel Ticket", "Cancel this ticket?", "Yes", "No");
        if (!confirm) return;
        var result = await _importedService.CancelAsync(ticket.Id);
        if (result.Success) await LoadTickets();
    }

    [RelayCommand]
    private static async Task GoToLogin()
    {
        App.GoToLogin();
        await Task.CompletedTask;
    }
}
