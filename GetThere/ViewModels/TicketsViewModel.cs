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

    [RelayCommand]
    private static async Task GoToLogin()
    {
        App.GoToLogin();
        await Task.CompletedTask;
    }
}
