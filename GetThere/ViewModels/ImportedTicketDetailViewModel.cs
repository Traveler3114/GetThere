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
        // Downgraded where the dates say so. The server's sweep runs hourly, so a window that shut
        // minutes ago still reads Active — and this screen is exactly where that matters.
        var pastValidity = TicketValidity.IsPastValidity(
            ticket.Status == GetThereShared.Enums.ImportedTicketStatus.Active, ticket.ValidTo, DateTime.UtcNow);

        StatusText = (pastValidity
            ? nameof(GetThereShared.Enums.ImportedTicketStatus.Expired)
            : ticket.Status.ToString()).ToUpperInvariant();
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

        // ── Known defect: an imported ticket's validity is a calendar date, not an instant, and
        //    ToLocalTime shifts it. Recorded rather than changed, because the same two lines are
        //    correct on the purchased screen and the split needs a device to check.
        //
        // The write side is deliberate and documented in ImportTicketViewModel.Save: the picked days
        // are sent unconverted (ValidFrom.Date, ValidTo.Date.AddDays(1).AddTicks(-1)), and
        // ImportedTicketManager.ToUtc stamps Unspecified as UTC "at face value". Nothing converts on
        // the way back either — GetThereAPI's DbContext has no DateTime value converter, so SQL
        // Server returns Unspecified and it serialises without a Z.
        //
        // So ToLocalTime here re-introduces exactly the shift the write side was fixed to avoid.
        // A ticket the user marked valid on 29 July renders as:
        //
        //     UTC+2 (Zagreb)     29 Jul · 02:00  →  30 Jul · 01:59     end date wrong
        //     UTC-5 (New York)   28 Jul · 19:00  →  29 Jul · 18:59     start date wrong
        //     UTC                29 Jul · 00:00  →  29 Jul · 23:59     correct
        //
        // Only exactly UTC displays it right. East of UTC the 23:59:59.9999999 end-of-day crosses
        // midnight; west of UTC the midnight start falls back a day.
        //
        // The list disagrees with this screen about the same field: WalletTicket keeps ValidFrom and
        // ValidTo raw and TicketValidity.IsPastValidity compares them against DateTime.UtcNow. So
        // the wallet list and this detail view can show different dates for one ticket.
        //
        // Why not simply drop the two ToLocalTime calls: TicketDetailViewModel has the identical
        // pair for *purchased* tickets, whose validity comes from an operator SDK
        // (TicketingManager: ValidFrom = result.Ticket.ValidFrom) and is a genuine instant, where
        // converting is right. JourneyDetailViewModel mixes both kinds in one list and already
        // carries Leg.IsImported for exactly this sort of distinction. Getting that split right is a
        // change to make with the app in front of you in a non-UTC timezone.
        DepartsText = ticket.ValidFrom?.ToLocalTime().ToString("dd MMM · HH:mm") ?? dash;
        ArrivesText = ticket.ValidTo?.ToLocalTime().ToString("dd MMM · HH:mm") ?? dash;

        PriceText = ticket.Price is { } price && !string.IsNullOrWhiteSpace(ticket.Currency)
            ? MoneyFormatter.Format(price, ticket.Currency)
            : dash;
    }
}
