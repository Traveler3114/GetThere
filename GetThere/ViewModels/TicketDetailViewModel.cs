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
    private readonly BarcodeRenderService _barcodeRenderer;

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

    /// <summary>
    /// The payload rendered as a scannable code, or null when it cannot safely be drawn as one —
    /// an unsupported format, or a payload the symbology will not round-trip. Null is expected, and
    /// the view falls back to printing <see cref="Payload"/> as text.
    /// </summary>
    [ObservableProperty]
    private ImageSource? _payloadImage;

    /// <summary>True when there is a code to show, so the view can pick a branch.</summary>
    [ObservableProperty]
    private bool _hasPayloadImage;

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

    public TicketDetailViewModel(TicketService ticketService, BarcodeRenderService barcodeRenderer)
    {
        _ticketService = ticketService;
        _barcodeRenderer = barcodeRenderer;
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
        // Display only — the server owns the status, and a window that closed since this was read
        // must not still show as active on the screen someone holds up at a barrier.
        var recordedActive = ticket.Status == TicketStatus.Active;
        var pastValidity = TicketValidity.IsPastValidity(recordedActive, ticket.ValidTo, DateTime.UtcNow);

        IsActive = recordedActive && !pastValidity;
        StatusText = (pastValidity ? nameof(TicketStatus.Expired) : ticket.Status.ToString()).ToUpperInvariant();

        Payload = ticket.Data;

        // ticket.Format is the adapter's own description of the payload. It was never read before
        // this — the screen printed the string and left the format unused.
        PayloadImage = _barcodeRenderer.Render(ticket.Data, ticket.Format);
        HasPayloadImage = PayloadImage is not null;

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
