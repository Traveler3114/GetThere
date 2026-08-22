using System.Text.Json;

using GetThereShared.Contracts;

namespace GetThere.Services;

/// <summary>
/// The one bridge between the map WebView and the app, and it is one-way: the map page emits the
/// selected routed itinerary by navigating to the <c>gtapp://journey</c> scheme,
/// <see cref="GetThere.Pages.MapPage"/> cancels that navigation and stores the legs here, and
/// <see cref="GetThere.ViewModels.BuyJourneyViewModel"/> picks them up to price and buy the journey
/// through GetThereAPI.
/// <para>
/// This is not the removed native→JavaScript chrome/token bridge: nothing is injected into the
/// page, and the page still never calls GetThereAPI. The WebView only hands routing facts out.
/// </para>
/// </summary>
public class JourneyHandoff
{
    /// <summary>camelCase options, matching planner.js's <c>JSON.stringify</c> output.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>The quoted legs of the itinerary the user tapped "Get tickets" on, or null when none.</summary>
    public List<QuoteLegDto>? PendingLegs { get; set; }
}
