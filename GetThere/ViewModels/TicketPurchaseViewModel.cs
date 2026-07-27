using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;
using GetThere.Services;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.ViewModels;

/// <summary>
/// One selectable fare on the purchase screen. Selection is a view concern, so it lives here
/// rather than on the contract type.
/// </summary>
public partial class PurchaseOption : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public required TicketOptionResponse Source { get; init; }

    public int Id => Source.Id;
    public string Name => Source.Name;
    public decimal Price => Source.Price;
    public string PriceText => MoneyFormatter.Format(Source.Price, Source.Currency);

    /// <summary>"45 min · QR" — duration where the fare has one, plus the delivery format.</summary>
    public string Detail
    {
        get
        {
            var duration = Source.DurationMinutes is > 0
                ? string.Format(LocalizationService.Instance["Purchase_DurationMinutes"], Source.DurationMinutes)
                : LocalizationService.Instance["Purchase_OpenEnded"];

            return string.IsNullOrWhiteSpace(Source.Description)
                ? $"{duration} · {Source.TicketFormat}"
                : $"{duration} · {Source.Description}";
        }
    }
}

/// <summary>
/// Frame 3c — the operator's fare list, the wallet footer and the commit action.
/// Reached as <c>ticketpurchase?adapterId=N</c> from the Shop.
/// </summary>
[QueryProperty(nameof(AdapterId), "adapterId")]
public partial class TicketPurchaseViewModel : BaseViewModel
{
    private readonly TicketService _ticketService;
    private readonly WalletService _walletService;
    private readonly IAnalyticsService _analytics;

    private string _adapterId = string.Empty;

    /// <summary>Set by Shell from the route query; triggers the load.</summary>
    public string AdapterId
    {
        get => _adapterId;
        set
        {
            _adapterId = value;
            if (int.TryParse(value, out var parsed))
                _ = LoadAsync(parsed);
        }
    }

    [ObservableProperty]
    private string _operatorName = string.Empty;

    [ObservableProperty]
    private string _operatorType = string.Empty;

    [ObservableProperty]
    private string _walletBalanceText = string.Empty;

    [ObservableProperty]
    private string _selectedPriceText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _hasOptions;

    /// <summary>
    /// True only once a load has finished and found nothing. Separate from <c>!HasOptions</c> so
    /// the empty message does not flash while the fare list is still being fetched.
    /// </summary>
    [ObservableProperty]
    private bool _hasNoOptions;

    /// <summary>False while nothing is selected or the wallet cannot cover the fare.</summary>
    [ObservableProperty]
    private bool _canConfirm;

    public ObservableCollection<PurchaseOption> Options { get; } = [];

    private decimal _walletBalance;
    private PurchaseOption? _selected;

    /// <summary>Idempotency key for the current selection; see <see cref="Confirm"/>.</summary>
    private string? _idempotencyKey;

    public TicketPurchaseViewModel(TicketService ticketService, WalletService walletService, IAnalyticsService analytics)
    {
        _ticketService = ticketService;
        _walletService = walletService;
        _analytics = analytics;
    }

    private async Task LoadAsync(int adapterId)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            var walletTask = _walletService.GetWalletAsync();
            var optionsTask = _ticketService.GetTicketOptionsAsync();
            await Task.WhenAll(walletTask, optionsTask);

            var wallet = walletTask.Result;
            if (wallet.Success && wallet.Data is not null)
            {
                _walletBalance = wallet.Data.Balance;
                WalletBalanceText = string.Format(
                    LocalizationService.Instance["Purchase_Available"],
                    wallet.Data.FormattedBalance);
            }

            var options = optionsTask.Result;
            if (!options.Success)
            {
                ErrorText = options.Message ?? LocalizationService.Instance["Error_CouldNotLoadOptions"];
                HasError = true;
                return;
            }

            var mine = (options.Data ?? [])
                .Where(o => o.AdapterId == adapterId)
                .OrderBy(o => o.Price)
                .ToList();

            OperatorName = mine.FirstOrDefault()?.AdapterName ?? string.Empty;
            OperatorType = mine.FirstOrDefault()?.AdapterType ?? string.Empty;

            Options.Clear();
            foreach (var option in mine)
                Options.Add(new PurchaseOption { Source = option });

            HasOptions = Options.Count > 0;

            // The design shows the first (cheapest) fare pre-selected.
            if (Options.Count > 0)
                Select(Options[0]);
            else
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
            HasNoOptions = !HasOptions && !HasError;
        }
    }

    [RelayCommand]
    private void Select(PurchaseOption? option)
    {
        if (option is null)
            return;

        foreach (var candidate in Options)
            candidate.IsSelected = ReferenceEquals(candidate, option);

        _selected = option;
        SelectedPriceText = option.PriceText;

        // A new selection is a new intent, so it gets a new idempotency key. Retries of *this*
        // selection reuse it, which is what stops a tap-after-timeout becoming a second charge.
        _idempotencyKey = TicketService.NewIdempotencyKey();

        UpdateCommitState();
    }

    private void UpdateCommitState()
    {
        if (_selected is null)
        {
            CanConfirm = false;
            return;
        }

        var affordable = _walletBalance >= _selected.Price;
        CanConfirm = affordable;

        if (!affordable)
        {
            ErrorText = LocalizationService.Instance["Purchase_InsufficientFunds"];
            HasError = true;
        }
        else if (HasError && ErrorText == LocalizationService.Instance["Purchase_InsufficientFunds"])
        {
            HasError = false;
            ErrorText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task Confirm()
    {
        if (_selected is null || IsBusy || !CanConfirm)
            return;

        IsBusy = true;
        HasError = false;
        try
        {
            var result = await _ticketService.PurchaseTicketAsync(
                new PurchaseTicketRequest
                {
                    AdapterId = _selected.Source.AdapterId,
                    OptionId = _selected.Id
                },
                // Same key across retries of this selection: if the first attempt actually reached the
                // server and the response was lost, the server replays that ticket instead of charging
                // the wallet twice.
                _idempotencyKey ??= TicketService.NewIdempotencyKey());

            if (!result.Success || result.Data is null)
            {
                ErrorText = result.Message ?? LocalizationService.Instance["Error_PurchaseFailed"];
                HasError = true;
                return;
            }

            _analytics.TrackEvent("ticket_purchased", new()
            {
                ["adapter"] = _selected.Source.AdapterName,
                ["option"] = _selected.Name
            });

            // Straight to the ticket — frame 3c hands off to 3d.
            await Shell.Current.GoToAsync($"ticketdetail?ticketId={result.Data.Id}");
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

    [RelayCommand]
    private static async Task Back() => await Shell.Current.GoToAsync("..");
}
