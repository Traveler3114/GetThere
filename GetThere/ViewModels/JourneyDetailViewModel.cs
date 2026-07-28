using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;
using GetThere.Services;

using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

namespace GetThere.ViewModels;

/// <summary>
/// One leg of a journey, wrapped for display.
/// <para>
/// <see cref="JourneyLegResponse.Id"/> alone does not identify a leg — imported and purchased
/// tickets share an id space only by accident — so every action here carries
/// <see cref="JourneyLegResponse.IsImported"/> with it. Getting that wrong removes the wrong ticket.
/// </para>
/// </summary>
public partial class JourneyLegItem : ObservableObject
{
    public required JourneyLegResponse Leg { get; init; }

    public string Title => FirstNonBlank(
        Leg.TicketName,
        Leg.RouteDescription,
        Route,
        LocalizationService.Instance["Journey_UntitledLeg"]);

    /// <summary>"Zagreb → Rijeka" when both endpoints are known, otherwise whichever one is.</summary>
    public string Route =>
        !string.IsNullOrWhiteSpace(Leg.OriginName) && !string.IsNullOrWhiteSpace(Leg.DestinationName)
            ? $"{Leg.OriginName} → {Leg.DestinationName}"
            : FirstNonBlank(Leg.RouteDescription, Leg.OriginName, Leg.DestinationName, string.Empty);

    public bool HasRoute => !string.IsNullOrWhiteSpace(Route) && Route != Title;

    public string Operator => Leg.OperatorNameSnapshot ?? string.Empty;

    public bool HasOperator => !string.IsNullOrWhiteSpace(Operator);

    public string Status => Leg.Status;

    public string When
    {
        get
        {
            var from = Leg.ValidFrom?.ToLocalTime();
            var to = Leg.ValidTo?.ToLocalTime();

            if (from is null && to is null)
                return LocalizationService.Instance["Journey_NoDates"];

            if (from is not null && to is not null)
            {
                // Same day reads as one date plus a time range; the full date twice is noise.
                return from.Value.Date == to.Value.Date
                    ? $"{from.Value:dd MMM} · {from.Value:HH:mm}–{to.Value:HH:mm}"
                    : $"{from.Value:dd MMM HH:mm} → {to.Value:dd MMM HH:mm}";
            }

            return (from ?? to)!.Value.ToString("dd MMM HH:mm");
        }
    }

    public string PriceText =>
        Leg.Price.HasValue ? MoneyFormatter.Format(Leg.Price.Value, Leg.Currency) : string.Empty;

    public bool HasPrice => Leg.Price.HasValue;

    /// <summary>Distinguishes an imported leg from a purchased one in the UI.</summary>
    public string SourceLabel => LocalizationService.Instance[
        Leg.IsImported ? "Journey_LegImported" : "Journey_LegPurchased"];

    private static string FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;
}

/// <summary>
/// A single trip and its legs. Reached as <c>journeydetail?journeyId=N</c>.
/// </summary>
[QueryProperty(nameof(JourneyId), "journeyId")]
public partial class JourneyDetailViewModel : BaseViewModel
{
    private readonly JourneyService _journeyService;
    private readonly IAnalyticsService _analytics;

    private string _journeyId = string.Empty;
    private int _loadedId;

    public string JourneyId
    {
        get => _journeyId;
        set
        {
            _journeyId = value;
            if (int.TryParse(value, out var parsed))
                _ = LoadAsync(parsed);
        }
    }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _hasNotes;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _dateRangeText = string.Empty;

    [ObservableProperty]
    private string _legCountText = string.Empty;

    [ObservableProperty]
    private string _totalText = string.Empty;

    /// <summary>
    /// False when legs are priced in more than one currency. There is no conversion anywhere in the
    /// system, so summing across currencies would produce a number that is simply wrong.
    /// </summary>
    [ObservableProperty]
    private bool _hasTotal;

    [ObservableProperty]
    private bool _hasJourney;

    [ObservableProperty]
    private bool _hasLegs;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public ObservableCollection<JourneyLegItem> Legs { get; } = [];

    public JourneyDetailViewModel(JourneyService journeyService, IAnalyticsService analytics)
    {
        _journeyService = journeyService;
        _analytics = analytics;
    }

    private async Task LoadAsync(int journeyId)
    {
        _loadedId = journeyId;
        IsBusy = true;
        HasError = false;

        try
        {
            var result = await _journeyService.GetByIdAsync(journeyId);
            if (!result.Success || result.Data is null)
            {
                Fail(result.Message);
                return;
            }

            Apply(result.Data);
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally { IsBusy = false; }
    }

    private void Apply(JourneyResponse journey)
    {
        Name = journey.Name;
        Notes = journey.Notes ?? string.Empty;
        HasNotes = !string.IsNullOrWhiteSpace(Notes);

        StatusText = LocalizationService.Instance[$"JourneyStatus_{journey.Status}"];
        CanCancel = journey.Status is not (JourneyStatus.Cancelled or JourneyStatus.Completed);

        DateRangeText = FormatRange(journey.StartsAt, journey.EndsAt);

        LegCountText = string.Format(
            LocalizationService.Instance["Journey_LegCount"], journey.LegCount);

        Legs.Clear();
        foreach (var leg in journey.Legs)
            Legs.Add(new JourneyLegItem { Leg = leg });

        HasLegs = Legs.Count > 0;
        ApplyTotal(journey.Legs);

        HasJourney = true;
    }

    /// <summary>
    /// Sums the legs, but only when they agree on a currency. Mixed currencies show no total rather
    /// than a meaningless one — the same reason the API refuses a cross-currency purchase.
    /// </summary>
    private void ApplyTotal(List<JourneyLegResponse> legs)
    {
        var priced = legs.Where(l => l.Price.HasValue).ToList();
        if (priced.Count == 0)
        {
            HasTotal = false;
            return;
        }

        var currencies = priced
            .Select(l => l.Currency ?? SupportedCurrencies.Default)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (currencies.Count > 1)
        {
            HasTotal = false;
            return;
        }

        TotalText = MoneyFormatter.Format(priced.Sum(l => l.Price!.Value), currencies[0]);
        HasTotal = true;
    }

    private static string FormatRange(DateTime? startsAt, DateTime? endsAt)
    {
        var from = startsAt?.ToLocalTime();
        var to = endsAt?.ToLocalTime();

        if (from is null && to is null)
            return LocalizationService.Instance["Journey_NoDates"];

        if (from is not null && to is not null)
        {
            return from.Value.Date == to.Value.Date
                ? from.Value.ToString("dd MMM yyyy")
                : $"{from.Value:dd MMM} – {to.Value:dd MMM yyyy}";
        }

        return (from ?? to)!.Value.ToString("dd MMM yyyy");
    }

    [RelayCommand]
    private async Task Rename()
    {
        if (Shell.Current is null || IsBusy) return;

        var name = await Shell.Current.DisplayPromptAsync(
            LocalizationService.Instance["Journey_RenameTitle"],
            LocalizationService.Instance["Journey_RenamePrompt"],
            accept: LocalizationService.Instance["App_Save"],
            cancel: LocalizationService.Instance["App_Cancel"],
            initialValue: Name,
            maxLength: 200);

        if (string.IsNullOrWhiteSpace(name) || name.Trim() == Name) return;

        await MutateAsync(() => _journeyService.UpdateAsync(_loadedId, new UpdateJourneyRequest { Name = name.Trim() }));
    }

    /// <summary>
    /// Cancels the trip. Only <see cref="JourneyStatus.Cancelled"/> is settable by hand — the other
    /// states are recomputed from the legs server-side, so offering them would produce a value that
    /// silently reverts on the next sweep.
    /// </summary>
    [RelayCommand]
    private async Task Cancel()
    {
        if (Shell.Current is null || IsBusy) return;

        var confirmed = await Shell.Current.DisplayAlert(
            LocalizationService.Instance["Journey_CancelTitle"],
            LocalizationService.Instance["Journey_CancelPrompt"],
            LocalizationService.Instance["Journey_CancelConfirm"],
            LocalizationService.Instance["App_Cancel"]);

        if (!confirmed) return;

        await MutateAsync(() => _journeyService.UpdateAsync(
            _loadedId, new UpdateJourneyRequest { Status = JourneyStatus.Cancelled }));
    }

    /// <summary>
    /// Takes one leg out of the trip. The ticket itself is untouched — it returns to the wallet
    /// ungrouped — so this needs no destructive-sounding confirmation.
    /// </summary>
    [RelayCommand]
    private async Task RemoveLeg(JourneyLegItem? item)
    {
        if (item is null || Shell.Current is null || IsBusy) return;

        var confirmed = await Shell.Current.DisplayAlert(
            LocalizationService.Instance["Journey_RemoveLegTitle"],
            LocalizationService.Instance["Journey_RemoveLegPrompt"],
            LocalizationService.Instance["Journey_RemoveLegConfirm"],
            LocalizationService.Instance["App_Cancel"]);

        if (!confirmed) return;

        // IsImported decides which list the id goes in. The two ticket tables share an id space by
        // accident, so putting it in the wrong one would remove a different ticket entirely.
        var request = item.Leg.IsImported
            ? new JourneyMembershipRequest { ImportedTicketIds = [item.Leg.Id] }
            : new JourneyMembershipRequest { TicketIds = [item.Leg.Id] };

        await MutateAsync(() => _journeyService.RemoveTicketsAsync(_loadedId, request));
    }

    /// <summary>Deletes the trip. Its tickets are released, never deleted — the server nulls their JourneyId.</summary>
    [RelayCommand]
    private async Task Delete()
    {
        if (Shell.Current is null || IsBusy) return;

        var confirmed = await Shell.Current.DisplayAlert(
            LocalizationService.Instance["Journey_DeleteTitle"],
            LocalizationService.Instance["Journey_DeletePrompt"],
            LocalizationService.Instance["Journey_DeleteConfirm"],
            LocalizationService.Instance["App_Cancel"]);

        if (!confirmed) return;

        IsBusy = true;
        HasError = false;

        try
        {
            var result = await _journeyService.DeleteAsync(_loadedId);
            if (!result.Success)
            {
                Fail(result.Message);
                return;
            }

            _analytics.TrackEvent("journey_deleted");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally { IsBusy = false; }
    }

    /// <summary>Runs a call that returns the updated journey, and rebinds from its response.</summary>
    private async Task MutateAsync(Func<Task<OperationResult<JourneyResponse>>> action)
    {
        IsBusy = true;
        HasError = false;

        try
        {
            var result = await action();
            if (!result.Success || result.Data is null)
            {
                Fail(result.Message);
                return;
            }

            Apply(result.Data);
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally { IsBusy = false; }
    }

    private void Fail(string? message)
    {
        ErrorText = string.IsNullOrWhiteSpace(message)
            ? LocalizationService.Instance["Journeys_CouldNotLoad"]
            : message;
        HasError = true;
    }

    [RelayCommand]
    private static async Task Back() => await Shell.Current.GoToAsync("..");
}
