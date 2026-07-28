using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;
using GetThere.Services;

using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThere.ViewModels;

/// <summary>
/// Frame 3d — a single purchased ticket: gradient header, the QR the adapter issued, and the
/// departure/arrival/id/paid grid. Reached as <c>ticketdetail?ticketId=N</c>.
/// </summary>
[QueryProperty(nameof(TicketId), "ticketId")]
public partial class TicketDetailViewModel : BaseViewModel
{
    private readonly TicketService _ticketService;

    private string _ticketId = string.Empty;

    public string TicketId
    {
        get => _ticketId;
        set
        {
            _ticketId = value;
            if (int.TryParse(value, out var parsed))
                _ = LoadAsync(parsed);
        }
    }

    [ObservableProperty]
    private string _operatorLine = string.Empty;

    [ObservableProperty]
    private string _routeText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>The adapter's payload — a QR string, a barcode number, or a plain code.</summary>
    [ObservableProperty]
    private string _payload = string.Empty;

    [ObservableProperty]
    private string _issuedByText = string.Empty;

    [ObservableProperty]
    private string _departsText = string.Empty;

    [ObservableProperty]
    private string _arrivesText = string.Empty;

    [ObservableProperty]
    private string _ticketReference = string.Empty;

    [ObservableProperty]
    private string _paidText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _hasTicket;

    /// <summary>Drives the pulsing "verified" dot, which the design only shows on live tickets.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// True when this ticket has been grouped into a trip, which is what shows the "Show journey"
    /// action. A purchased ticket is a journey leg just as an imported one is.
    /// </summary>
    [ObservableProperty]
    private bool _hasJourney;

    private int? _journeyId;

    public TicketDetailViewModel(TicketService ticketService)
    {
        _ticketService = ticketService;
    }

    private async Task LoadAsync(int ticketId)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            var result = await _ticketService.GetMyTicketsAsync();
            if (!result.Success)
            {
                Fail(result.Message);
                return;
            }

            var ticket = (result.Data ?? []).FirstOrDefault(t => t.Id == ticketId);
            if (ticket is null)
            {
                Fail(LocalizationService.Instance["Ticket_CouldNotLoad"]);
                return;
            }

            Apply(ticket);
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(TicketResponse ticket)
    {
        var option = ticket.Option;
        var dash = LocalizationService.Instance["Ticket_NotAvailable"];

        OperatorLine = string.IsNullOrWhiteSpace(option.ExternalProductId)
            ? option.AdapterName
            : $"{option.AdapterName} · {option.ExternalProductId}";

        RouteText = option.Name;
        IsActive = ticket.Status == TicketStatus.Active;
        StatusText = ticket.Status.ToString().ToUpperInvariant();

        Payload = ticket.Data;
        IssuedByText = string.IsNullOrWhiteSpace(option.AdapterType)
            ? option.AdapterName
            : string.Format(LocalizationService.Instance["Ticket_AdapterSuffix"], option.AdapterType);

        DepartsText = ticket.ValidFrom?.ToLocalTime().ToString("dd MMM · HH:mm") ?? dash;
        ArrivesText = ticket.ValidTo?.ToLocalTime().ToString("dd MMM · HH:mm") ?? dash;

        TicketReference = string.IsNullOrWhiteSpace(ticket.ExternalTicketId)
            ? $"TKT-{ticket.Id}"
            : ticket.ExternalTicketId;

        PaidText = string.Format(
            LocalizationService.Instance["Ticket_PaidWallet"],
            MoneyFormatter.Format(option.Price, option.Currency));

        _journeyId = ticket.JourneyId;
        HasJourney = _journeyId.HasValue;

        HasTicket = true;
    }

    private void Fail(string? message)
    {
        ErrorText = message ?? LocalizationService.Instance["Ticket_CouldNotLoad"];
        HasError = true;
        HasTicket = false;
        HasJourney = false;
    }

    /// <summary>Opens the trip this ticket belongs to. Hidden when it belongs to none.</summary>
    [RelayCommand]
    private async Task ShowJourney()
    {
        if (_journeyId is not { } journeyId) return;
        await Shell.Current.GoToAsync($"journeydetail?journeyId={journeyId}");
    }

    [RelayCommand]
    private static async Task Back() => await Shell.Current.GoToAsync("..");
}
