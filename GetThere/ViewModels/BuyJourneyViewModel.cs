using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;
using GetThere.Services;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.ViewModels;

/// <summary>
/// One offer on the buy-journey screen, wrapped for display. The fulfillment badge and the
/// post-booking outcome are view concerns, so they live here rather than on the contract type.
/// </summary>
public partial class BuyOfferItem : ObservableObject
{
    public required OperatorOfferDto Source { get; init; }

    public string OperatorName => Source.OperatorName;

    public string ProductName => Source.ProductName ?? string.Empty;
    public bool HasProduct => !string.IsNullOrWhiteSpace(ProductName);

    /// <summary>Server note shown when the offer has no product (e.g. unpriced buy-on-board).</summary>
    public string Note => Source.Note ?? string.Empty;
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public bool HasPrice => Source.Price.HasValue;
    public string PriceText => MoneyFormatter.Format(Source.Price ?? 0m, Source.Currency);

    /// <summary>"Buy now" vs "On board", from the fulfillment mode the API returned.</summary>
    public string FulfillmentBadgeText => Source.FulfillmentMode == FulfillmentModes.PurchasableNow
        ? LocalizationService.Instance["BuyJourney_FulfillmentNow"]
        : LocalizationService.Instance["BuyJourney_FulfillmentBoard"];

    public bool IsBuyNow => Source.FulfillmentMode == FulfillmentModes.PurchasableNow;

    [ObservableProperty]
    private string _outcomeText = string.Empty;

    [ObservableProperty]
    private bool _hasOutcome;

    public void SetOutcome(BookedOfferDto booked)
    {
        OutcomeText = booked.Outcome switch
        {
            BookingOutcomes.Purchased => LocalizationService.Instance["BuyJourney_OutcomePurchased"],
            BookingOutcomes.Reserved => LocalizationService.Instance["BuyJourney_OutcomeReserved"],
            BookingOutcomes.BuyOnBoardUnpriced => LocalizationService.Instance["BuyJourney_OutcomeOnBoard"],
            _ => booked.Outcome
        };
        HasOutcome = true;
    }
}

/// <summary>
/// Prices a routed itinerary handed over by the map (see <see cref="GetThere.Services.JourneyHandoff"/>)
/// and buys it: the purchasable-now legs are charged to the wallet, the buy-on-board legs have their
/// funds held. Reached as <c>buyjourney</c> from MapPage's WebView handoff.
/// </summary>
public partial class BuyJourneyViewModel : BaseViewModel
{
    private readonly JourneyHandoff _handoff;
    private readonly JourneyService _journeyService;
    private readonly WalletService _walletService;

    private List<QuoteLegDto>? _legs;
    private int _journeyId;
    private bool _hasLoaded;

    public BuyJourneyViewModel(JourneyHandoff handoff, JourneyService journeyService, WalletService walletService)
    {
        _handoff = handoff;
        _journeyService = journeyService;
        _walletService = walletService;
    }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _walletAvailableText = string.Empty;

    [ObservableProperty]
    private string _walletReservedText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _hasOffers;

    /// <summary>True when the screen opened without a handed-over itinerary — nothing to buy.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>True once the journey has been booked; arms the Cancel button and disarms Buy all.</summary>
    [ObservableProperty]
    private bool _hasBooked;

    [ObservableProperty]
    private bool _canBuyAll;

    partial void OnHasBookedChanged(bool value) => UpdateCommitState();

    public ObservableCollection<BuyOfferItem> Offers { get; } = [];

    /// <summary>The page's OnAppearing hook — the handoff arrives before navigation, not in a query.</summary>
    [RelayCommand]
    private async Task Load()
    {
        if (_hasLoaded)
            return;
        _hasLoaded = true;

        IsBusy = true;
        HasError = false;
        try
        {
            var legs = _handoff.PendingLegs;
            if (legs is null || legs.Count == 0)
            {
                IsEmpty = true;
                return;
            }

            _legs = legs;
            // A fresh load is the only reader of the handoff: consume it so a stale itinerary
            // cannot be picked up again later.
            _handoff.PendingLegs = null;

            Name = DefaultName(legs);

            var walletTask = _walletService.GetWalletAsync();
            var quoteTask = _journeyService.QuoteAsync(new JourneyQuoteRequest(legs));
            await Task.WhenAll(walletTask, quoteTask);

            ApplyWallet(walletTask.Result);

            var quote = quoteTask.Result;
            if (!quote.Success || quote.Data is null)
            {
                ErrorText = quote.Message ?? LocalizationService.Instance["BuyJourney_CouldNotQuote"];
                HasError = true;
                return;
            }

            Offers.Clear();
            foreach (var offer in quote.Data.Offers)
                Offers.Add(new BuyOfferItem { Source = offer });

            HasOffers = Offers.Count > 0;
            TotalText = MoneyFormatter.Format(quote.Data.Total, quote.Data.Currency);

            UpdateCommitState();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Buys every offer at once: the purchasable-now legs come out of the wallet, the buy-on-board
    /// legs have their funds held. The server's journey id comes back in the response.
    /// </summary>
    [RelayCommand]
    private async Task BuyAll()
    {
        if (IsBusy || !CanBuyAll || _legs is null)
            return;

        IsBusy = true;
        HasError = false;
        CanBuyAll = false;
        try
        {
            var result = await _journeyService.BookAsync(new BookJourneyRequest(Name.Trim(), _legs));
            if (!result.Success || result.Data is null)
            {
                ErrorText = result.Message ?? LocalizationService.Instance["BuyJourney_BookingFailed"];
                HasError = true;
                return;
            }

            var booking = result.Data;
            _journeyId = booking.JourneyId;
            Name = booking.Name;

            // Report what actually happened to each operator segment.
            foreach (var item in Offers)
            {
                var booked = booking.Items.FirstOrDefault(b => b.OperatorGlobalId == item.Source.OperatorGlobalId);
                if (booked is not null)
                    item.SetOutcome(booked);
            }

            await RefreshWalletAsync();

            // Straight to the booked journey, as the ticket purchase screen goes straight to its ticket.
            await Shell.Current.GoToAsync($"journeydetail?journeyId={_journeyId}");
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            HasError = true;
        }
        finally
        {
            IsBusy = false;
            UpdateCommitState();
        }
    }

    /// <summary>Releases a booked journey's held funds; they return to Available.</summary>
    [RelayCommand]
    private async Task CancelBooking()
    {
        if (IsBusy || !HasBooked || _journeyId == 0)
            return;

        IsBusy = true;
        HasError = false;
        CanBuyAll = false;
        try
        {
            var result = await _journeyService.CancelBookingAsync(_journeyId);
            if (!result.Success)
            {
                ErrorText = result.Message ?? LocalizationService.Instance["BuyJourney_CouldNotCancel"];
                HasError = true;
                return;
            }

            HasBooked = false;
            await RefreshWalletAsync();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            HasError = true;
        }
        finally
        {
            IsBusy = false;
            UpdateCommitState();
        }
    }

    [RelayCommand]
    private static async Task Back() => await Shell.Current.GoToAsync("..");

    private void UpdateCommitState() => CanBuyAll = HasOffers && !IsBusy && !HasBooked;

    /// <summary>
    /// "Trip to …" — the handoff carries coordinates only (no stop names), so the default name is a
    /// timestamp of the first transit leg's departure rather than a destination label.
    /// </summary>
    private static string DefaultName(List<QuoteLegDto> legs)
    {
        var starts = legs.Where(l => l.IsTransit).Select(l => l.StartTime).ToList();
        var when = starts.Count > 0 ? starts.Min().ToLocalTime() : DateTime.Now;
        return string.Format(LocalizationService.Instance["BuyJourney_DefaultName"], when.ToString("dd MMM HH:mm"));
    }

    private async Task RefreshWalletAsync() => ApplyWallet(await _walletService.GetWalletAsync());

    private void ApplyWallet(OperationResult<WalletResponse>? walletResult)
    {
        if (walletResult is not { Success: true, Data: not null })
            return;

        WalletAvailableText = walletResult.Data.FormattedAvailable;
        WalletReservedText = walletResult.Data.FormattedReserved;
    }
}