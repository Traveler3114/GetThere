using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;

using GetThere.Localization;
using GetThere.Services;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.ViewModels;

/// <summary>
/// A single imported ticket, with its barcode.
/// <para>
/// Imported tickets had no detail screen at all. The list offered an action sheet and nothing else,
/// so <c>RawPayload</c> — the thing a barrier actually scans, decoded from the file the user
/// imported — was written on import and never shown again. For a wallet whose premise is holding
/// tickets the user already had, that was the missing half.
/// </para>
/// </summary>
[QueryProperty(nameof(TicketId), "ticketId")]
public partial class ImportedTicketDetailViewModel : BaseViewModel
{
    private readonly ImportedTicketService _importedService;
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
    private string _ticketName = string.Empty;

    [ObservableProperty]
    private string _routeText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _operatorLine = string.Empty;

    [ObservableProperty]
    private string _payload = string.Empty;

    /// <summary>Null when the payload cannot safely be drawn; the view falls back to the text.</summary>
    [ObservableProperty]
    private ImageSource? _payloadImage;

    [ObservableProperty]
    private bool _hasPayloadImage;

    /// <summary>
    /// True when the ticket carries no scannable payload at all — imported by hand, or from a file
    /// with no barcode in it. Distinct from "has a payload we chose not to redraw", because the
    /// honest thing to tell the user differs.
    /// </summary>
    [ObservableProperty]
    private bool _hasNoPayload;

    /// <summary>
    /// There is a payload but no image — the renderer declined to redraw it. The text is still worth
    /// showing, since it is what the user has. A separate flag rather than a multi-binding so the
    /// view stays free of converter gymnastics for a three-way choice.
    /// </summary>
    [ObservableProperty]
    private bool _hasPayloadText;

    [ObservableProperty]
    private string _departsText = string.Empty;

    [ObservableProperty]
    private string _arrivesText = string.Empty;

    [ObservableProperty]
    private string _priceText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public ImportedTicketDetailViewModel(
        ImportedTicketService importedService,
        BarcodeRenderService barcodeRenderer)
    {
        _importedService = importedService;
        _barcodeRenderer = barcodeRenderer;
    }

    private async Task LoadAsync(int ticketId)
    {
        IsBusy = true;
        HasError = false;
        IsOffline = false;
        try
        {
            // A real by-id endpoint, unlike the purchased side, which has to fetch the whole
            // collection and filter it.
            var result = await _importedService.GetByIdAsync(ticketId);

            if (result.Success && result.Data is not null)
            {
                Apply(result.Data);
                return;
            }

            IsOffline = IsOfflineNow;
            ErrorText = IsOffline
                ? LocalizationService.Instance["Common_Offline"]
                : result.Message ?? "Could not load that ticket.";
            HasError = true;
        }
        catch (Exception ex)
        {
            IsOffline = IsOfflineNow;
            ErrorText = IsOffline ? LocalizationService.Instance["Common_Offline"] : ex.Message;
            HasError = true;
            Trace.WriteLine($"[ImportedTicketDetailViewModel] Load failed: {ex.Message}");
        }
        finally { IsBusy = false; }
    }

    private void Apply(ImportedTicketResponse ticket)
    {
        var dash = LocalizationService.Instance["Ticket_NotAvailable"];

        TicketName = ticket.TicketName ?? dash;
        RouteText = ticket.RouteDescription ?? string.Empty;
        StatusText = ticket.Status.ToString().ToUpperInvariant();
        OperatorLine = ticket.OperatorNameSnapshot ?? ticket.Source.ToString();

        Payload = ticket.RawPayload ?? string.Empty;
        HasNoPayload = string.IsNullOrWhiteSpace(ticket.RawPayload);

        // PayloadFormat is nullable — a hand-typed ticket has neither payload nor format. Where one
        // exists it came from the server's decoder, which is why it is trusted here as far as
        // ChooseSymbology is willing to trust it.
        PayloadImage = ticket.PayloadFormat is { } format
            ? _barcodeRenderer.Render(ticket.RawPayload, format)
            : null;
        HasPayloadImage = PayloadImage is not null;
        HasPayloadText = !HasNoPayload && !HasPayloadImage;

        DepartsText = ticket.ValidFrom?.ToLocalTime().ToString("dd MMM · HH:mm") ?? dash;
        ArrivesText = ticket.ValidTo?.ToLocalTime().ToString("dd MMM · HH:mm") ?? dash;

        PriceText = ticket.Price is { } price && !string.IsNullOrWhiteSpace(ticket.Currency)
            ? MoneyFormatter.Format(price, ticket.Currency)
            : dash;
    }
}
