using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;
using GetThere.Services;

using GetThereShared.Contracts;

namespace GetThere.ViewModels;

/// <summary>
/// The Journeys half of the tickets screen: the user's trips, plus the groupings the server proposes
/// over tickets that are not in one yet.
/// <para>
/// Owned by <see cref="TicketsViewModel"/> rather than bound to a page of its own, because journeys
/// and tickets are two views of the same wallet and the design puts them behind one segmented
/// control. It is still a separate view model so the two loads, error states and busy flags do not
/// interleave — a failing suggestions call must not blank the ticket list.
/// </para>
/// </summary>
public partial class JourneysViewModel : BaseViewModel
{
    private readonly JourneyService _journeyService;
    private readonly IAnalyticsService _analytics;

    private int _currentPage = 1;
    private const int PageSize = 50;

    [ObservableProperty]
    private bool _hasJourneys;

    [ObservableProperty]
    private bool _hasSuggestions;

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private int _totalJourneys;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// Set after the first load attempt, successful or not. <see cref="TicketsViewModel"/> reads it
    /// to load journeys the first time the segment is opened rather than on every page appearance —
    /// most sessions never open it. It is deliberately set even on failure, so a failed load shows
    /// its error instead of silently retrying on every switch.
    /// </summary>
    [ObservableProperty]
    private bool _hasLoadedOnce;

    public ObservableCollection<JourneyResponse> Journeys { get; } = [];

    public ObservableCollection<JourneySuggestionResponse> Suggestions { get; } = [];

    public JourneysViewModel(JourneyService journeyService, IAnalyticsService analytics)
    {
        _journeyService = journeyService;
        _analytics = analytics;
    }

    [RelayCommand]
    private async Task Load()
    {
        IsBusy = true;
        HasError = false;
        _currentPage = 1;

        try
        {
            var result = await _journeyService.ListAsync(page: _currentPage, perPage: PageSize);
            if (!result.Success)
            {
                Fail(result.Message, "Journeys_CouldNotLoad");
                return;
            }

            var paged = result.Data!;
            Journeys.Clear();
            foreach (var journey in paged.Data)
                Journeys.Add(journey);

            TotalJourneys = paged.Total;
            HasJourneys = Journeys.Count > 0;
            HasMore = _currentPage < paged.TotalPages;

            _analytics.TrackEvent("journeys_loaded", new() { ["count"] = Journeys.Count.ToString() });
        }
        catch (Exception ex)
        {
            Fail(ex.Message, "Journeys_CouldNotLoad");
        }
        finally
        {
            HasLoadedOnce = true;
            IsBusy = false;
        }

        // Deliberately after the list and outside its try: suggestions are a nice-to-have, and a
        // failure here must not leave the user staring at an error over a list that loaded fine.
        await LoadSuggestions();
    }

    private async Task LoadSuggestions()
    {
        try
        {
            var result = await _journeyService.GetSuggestionsAsync();
            Suggestions.Clear();

            if (result.Success)
            {
                foreach (var suggestion in result.Data ?? [])
                    Suggestions.Add(suggestion);
            }

            HasSuggestions = Suggestions.Count > 0;
        }
        catch
        {
            // Swallowed on purpose. There is no user-visible feature to degrade — the suggestions
            // card simply does not appear.
            HasSuggestions = false;
        }
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
            var result = await _journeyService.ListAsync(page: _currentPage, perPage: PageSize);
            if (result.Success)
            {
                foreach (var journey in result.Data!.Data)
                    Journeys.Add(journey);

                HasMore = _currentPage < result.Data.TotalPages;
            }
            else
            {
                _currentPage--;
                Fail(result.Message, "Journeys_CouldNotLoad");
            }
        }
        catch (Exception ex)
        {
            _currentPage--;
            Fail(ex.Message, "Journeys_CouldNotLoad");
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Turns a suggestion into a real journey. The server returns the ticket ids that make up the
    /// proposal, so accepting it is one create rather than a create followed by N adds.
    /// </summary>
    [RelayCommand]
    private async Task AcceptSuggestion(JourneySuggestionResponse? suggestion)
    {
        if (suggestion is null || IsBusy) return;

        IsBusy = true;
        HasError = false;

        try
        {
            var result = await _journeyService.CreateAsync(new CreateJourneyRequest
            {
                Name = suggestion.SuggestedName,
                ImportedTicketIds = suggestion.ImportedTicketIds,
                TicketIds = suggestion.TicketIds
            });

            if (!result.Success)
            {
                Fail(result.Message, "Journeys_CouldNotCreate");
                return;
            }

            _analytics.TrackEvent("journey_created", new()
            {
                ["source"] = "suggestion",
                ["legs"] = (suggestion.ImportedTicketIds.Count + suggestion.TicketIds.Count).ToString()
            });

            Suggestions.Remove(suggestion);
            HasSuggestions = Suggestions.Count > 0;
        }
        catch (Exception ex)
        {
            Fail(ex.Message, "Journeys_CouldNotCreate");

            // Returning matters: the reload below opens with HasError = false, so falling through
            // to it wiped the error just set and the user was told nothing at all. The non-success
            // branch above already returns; this path did not.
            return;
        }
        finally { IsBusy = false; }

        await Load();
    }

    /// <summary>Dismisses a proposal for this session. Nothing is persisted — it returns on reload.</summary>
    [RelayCommand]
    private void DismissSuggestion(JourneySuggestionResponse? suggestion)
    {
        if (suggestion is null) return;

        Suggestions.Remove(suggestion);
        HasSuggestions = Suggestions.Count > 0;
    }

    /// <summary>Creates an empty journey the user then adds tickets to from its detail screen.</summary>
    [RelayCommand]
    private async Task CreateJourney()
    {
        if (Shell.Current is null || IsBusy) return;

        var name = await Shell.Current.DisplayPromptAsync(
            LocalizationService.Instance["Journeys_NewTitle"],
            LocalizationService.Instance["Journeys_NewPrompt"],
            accept: LocalizationService.Instance["Common_Create"],
            cancel: LocalizationService.Instance["App_Cancel"],
            maxLength: 200);

        if (string.IsNullOrWhiteSpace(name)) return;

        IsBusy = true;
        HasError = false;

        try
        {
            var result = await _journeyService.CreateAsync(new CreateJourneyRequest { Name = name.Trim() });
            if (!result.Success)
            {
                Fail(result.Message, "Journeys_CouldNotCreate");
                return;
            }

            _analytics.TrackEvent("journey_created", new() { ["source"] = "manual", ["legs"] = "0" });
            await OpenJourney(result.Data!.Id);
        }
        catch (Exception ex)
        {
            Fail(ex.Message, "Journeys_CouldNotCreate");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private static async Task Open(JourneyResponse? journey)
    {
        if (journey is null) return;
        await OpenJourney(journey.Id);
    }

    private static Task OpenJourney(int journeyId) =>
        Shell.Current.GoToAsync($"journeydetail?journeyId={journeyId}");

    private void Fail(string? message, string fallbackKey)
    {
        ErrorText = string.IsNullOrWhiteSpace(message)
            ? LocalizationService.Instance[fallbackKey]
            : message;
        HasError = true;
    }
}
