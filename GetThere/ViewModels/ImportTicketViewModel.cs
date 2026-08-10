using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;
using GetThere.Services;

using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThere.ViewModels;

/// <summary>
/// The single confirmation surface for every import path. A photographed code, a wallet pass, a
/// pasted email and a hand-typed ticket all land here as an editable draft, so there is one
/// validation path rather than one per source, and nothing reaches the server unreviewed.
/// </summary>
public partial class ImportTicketViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ImportedTicketService _importedService;
    private readonly PendingImportQueue _queue;
    private readonly AuthService _authService;
    private readonly IAnalyticsService _analytics;

    [ObservableProperty]
    private string _ticketName = string.Empty;
    [ObservableProperty]
    private string _routeDescription = string.Empty;
    [ObservableProperty]
    private string _originName = string.Empty;
    [ObservableProperty]
    private string _destinationName = string.Empty;
    [ObservableProperty]
    private string _priceText = string.Empty;
    [ObservableProperty]
    private DateTime _validFrom = DateTime.Today;
    [ObservableProperty]
    private DateTime _validTo = DateTime.Today.AddDays(1);
    [ObservableProperty]
    private string _operatorName = string.Empty;
    [ObservableProperty]
    private string _errorText = string.Empty;
    [ObservableProperty]
    private bool _hasError;
    [ObservableProperty]
    private string _selectedCurrency = SupportedCurrencies.Default;

    /// <summary>Headline telling the user what was read off their file, or that nothing was.</summary>
    [ObservableProperty]
    private string _draftSummary = string.Empty;
    [ObservableProperty]
    private bool _hasDraft;

    /// <summary>The extractor's own caveat, such as an image with no code in it.</summary>
    [ObservableProperty]
    private string _warningText = string.Empty;
    [ObservableProperty]
    private bool _hasWarning;

    // Provenance carried from the capture step. Not editable: the blob key is a single-use handle
    // the server minted, and the source must match the file that was actually uploaded.
    private ImportSource _source = ImportSource.Manual;
    private string? _sourceFileBlobKey;
    private string? _rawPayload;
    private TicketFormat? _payloadFormat;

    public string[] Currencies => SupportedCurrencies.Selectable;

    public ImportTicketViewModel(
        ImportedTicketService importedService,
        PendingImportQueue queue,
        AuthService authService,
        IAnalyticsService analytics)
    {
        _importedService = importedService;
        _queue = queue;
        _authService = authService;
        _analytics = analytics;
    }

    /// <summary>
    /// Prefills from a captured draft. Every value is a suggestion the user can overwrite before
    /// saving — the fields stay editable precisely because extraction is best-effort.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue(TicketImportDraft.QueryKey, out var value) || value is not TicketImportDraft draft)
            return;

        _source = draft.Source;
        _sourceFileBlobKey = draft.BlobKey;

        var extraction = draft.Extraction;
        _rawPayload = extraction.RawPayload;
        _payloadFormat = extraction.PayloadFormat;

        if (!string.IsNullOrWhiteSpace(extraction.TicketName)) TicketName = extraction.TicketName;
        if (!string.IsNullOrWhiteSpace(extraction.RouteDescription)) RouteDescription = extraction.RouteDescription;
        if (!string.IsNullOrWhiteSpace(extraction.OriginName)) OriginName = extraction.OriginName;
        if (!string.IsNullOrWhiteSpace(extraction.DestinationName)) DestinationName = extraction.DestinationName;
        if (!string.IsNullOrWhiteSpace(extraction.OperatorNameSnapshot)) OperatorName = extraction.OperatorNameSnapshot;

        // Invariant, to match how Save parses it back.
        if (extraction.Price is { } price)
            PriceText = price.ToString(CultureInfo.InvariantCulture);

        // Only adopt a currency the picker can actually show, so the selection never disagrees with
        // what is displayed. An unknown code leaves the default in place for the user to correct.
        if (SupportedCurrencies.Selectable.Contains(extraction.Currency, StringComparer.OrdinalIgnoreCase))
            SelectedCurrency = extraction.Currency!.ToUpperInvariant();

        // Taken as the calendar day the extractor read off the ticket, not converted through the
        // machine's timezone. ToLocalTime shifted a date scraped as 15.08 to 14.08 for any user west
        // of UTC, because the extractor emits midnight UTC for a date it found written as "15.08.2026".
        if (extraction.ValidFrom is { } from) ValidFrom = from.Date;
        if (extraction.ValidTo is { } to) ValidTo = to.Date;

        if (ValidTo < ValidFrom) ValidTo = ValidFrom;

        HasDraft = true;
        DraftSummary = SummariseFor(extraction);

        WarningText = extraction.Warning ?? string.Empty;
        HasWarning = !string.IsNullOrEmpty(WarningText);
    }

    private static string SummariseFor(TicketExtractionResult extraction)
    {
        var detected = extraction.DetectedFields.Count;
        return detected == 0
            ? "We could not read any details from that — fill them in below."
            : $"We read {detected} detail{(detected == 1 ? "" : "s")} from your ticket. Check them and correct anything wrong.";
    }

    [RelayCommand]
    private async Task Save()
    {
        HasError = false;

        if (string.IsNullOrWhiteSpace(TicketName))
        {
            ErrorText = LocalizationService.Instance["Error_TicketNameRequired"]; HasError = true; return;
        }
        // Sent as the calendar days the pickers show, with no timezone conversion. A ticket's
        // validity is a date printed on the ticket, not an instant: marking the picked day Local and
        // converting to UTC moved it back by the machine's offset, so choosing 29.07 in Zagreb stored
        // 2026-07-28T22:00:00Z and every list then rendered "28.07". The API takes an offset-less
        // value at face value (see ImportedTicketManager.ToUtc), so an unshifted day round-trips
        // unchanged. ValidTo still spans to the end of its day.
        var validFrom = ValidFrom.Date;
        var validTo = ValidTo.Date.AddDays(1).AddTicks(-1);
        if (validTo <= validFrom)
        {
            ErrorText = LocalizationService.Instance["Error_ValidToBeforeValidFrom"]; HasError = true; return;
        }

        decimal? price = null;
        if (!string.IsNullOrWhiteSpace(PriceText))
        {
            // Invariant after normalising the separator: parsing under the current culture meant a
            // Croatian locale read the "12.50" this form produces as 1250, since "." is its group
            // separator. The app ships an HR translation, so that was reachable.
            const NumberStyles PriceStyles =
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

            if (!decimal.TryParse(PriceText.Replace(',', '.'), PriceStyles, CultureInfo.InvariantCulture, out var p) || p < 0)
            {
                ErrorText = LocalizationService.Instance["Error_InvalidPrice"]; HasError = true; return;
            }
            price = p;
        }

        IsBusy = true;
        try
        {
            var request = new CreateImportedTicketRequest
            {
                Source = _source,
                SourceFileBlobKey = _sourceFileBlobKey,
                TicketName = TicketName.Trim(),
                RouteDescription = string.IsNullOrWhiteSpace(RouteDescription) ? null : RouteDescription.Trim(),
                OriginName = string.IsNullOrWhiteSpace(OriginName) ? null : OriginName.Trim(),
                DestinationName = string.IsNullOrWhiteSpace(DestinationName) ? null : DestinationName.Trim(),
                Price = price,
                Currency = price.HasValue ? SelectedCurrency : null,
                ValidFrom = validFrom,
                ValidTo = validTo,
                OperatorNameSnapshot = string.IsNullOrWhiteSpace(OperatorName) ? null : OperatorName.Trim(),
                RawPayload = _rawPayload,
                PayloadFormat = _payloadFormat,

                // Minted here, at creation, not at push time. That is what makes a retry safe: the
                // id survives the app being killed mid-push, so the server recognises the second
                // attempt as the same ticket rather than a new one.
                ClientId = Guid.NewGuid()
            };

            // Signed out, or with no way to reach the server, the ticket is kept on the device and
            // pushed later. Importing used to require both an account and a connection, which for a
            // wallet whose premise is holding tickets the user already has was the wrong way round.
            if (!await _authService.IsLoggedInAsync() || IsOfflineNow)
            {
                await _queue.EnqueueAsync(request);
                _analytics.TrackEvent("ticket_imported_offline", new() { ["source"] = _source.ToString() });
                await Shell.Current.GoToAsync("..");
                return;
            }

            var result = await _importedService.CreateAsync(request);

            // A duplicate is a question, not a failure: two passengers on the same train on the same
            // day are a legitimate pair of tickets, and the server offers AllowDuplicate for exactly
            // this. Ask once, then resend.
            if (!result.Success && result.Code == ImportedTicketService.DuplicateCode)
            {
                var importAnyway = await Shell.Current.DisplayAlertAsync(
                    "Looks like a duplicate",
                    $"{result.Message}\n\nImport it anyway?",
                    "Import anyway", "Cancel");

                if (!importAnyway) return;

                request.AllowDuplicate = true;
                result = await _importedService.CreateAsync(request);
            }

            if (result.Success)
            {
                _analytics.TrackEvent("ticket_imported", new() { ["source"] = _source.ToString() });
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                // The server was reachable a moment ago and is not now, or refused for a reason that
                // is not the user's fault. Queue rather than lose what they just typed — the same
                // ClientId means the eventual push cannot double it.
                await _queue.EnqueueAsync(request);
                _analytics.TrackEvent("ticket_imported_offline", new() { ["source"] = _source.ToString() });
                await Shell.Current.GoToAsync("..");
            }
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            HasError = true;
        }
        finally { IsBusy = false; }
    }
}
