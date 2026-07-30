using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

using static GetThereShared.Common.HttpHelper;

namespace GetThere.Services;

/// <summary>
/// Talks to <c>/journeys</c>. Journeys group tickets — imported and purchased alike — into one trip.
/// <para>
/// Every call is scoped server-side to the caller's own id from the JWT, so there is no user
/// parameter anywhere here: a journey belonging to someone else is indistinguishable from one that
/// does not exist.
/// </para>
/// </summary>
public class JourneyService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public JourneyService(HttpClient http) { _http = http; }

    public async Task<OperationResult<PagedResult<JourneyResponse>>> ListAsync(
        int page = 1, int perPage = 50, JourneyStatus? status = null, CancellationToken ct = default)
    {
        try
        {
            var qs = new StringBuilder($"journeys?page={page}&perPage={perPage}");
            if (status.HasValue)
                qs.Append($"&status={status.Value}");

            var response = await _http.GetAsync(qs.ToString(), ct);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<PagedResult<JourneyResponse>>(JsonOptions, ct);
                return OperationResult<PagedResult<JourneyResponse>>.Ok(data!);
            }

            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult<PagedResult<JourneyResponse>>.Fail(problem ?? "Could not load journeys");
        }
        catch (Exception ex) { Trace.WriteLine($"[JourneyService] {ex}"); return OperationResult<PagedResult<JourneyResponse>>.Fail("Something went wrong. Check your connection and try again."); }
    }

    /// <summary>
    /// Loads one journey <b>with its legs</b>. The list endpoint leaves <c>Legs</c> empty to stay
    /// cheap, so the detail screen has to come through here rather than reusing a list item.
    /// </summary>
    public async Task<OperationResult<JourneyResponse>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"journeys/{id}", ct);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<JourneyResponse>(JsonOptions, ct);
                return OperationResult<JourneyResponse>.Ok(data!);
            }

            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult<JourneyResponse>.Fail(problem ?? "Could not load journey");
        }
        catch (Exception ex) { Trace.WriteLine($"[JourneyService] {ex}"); return OperationResult<JourneyResponse>.Fail("Something went wrong. Check your connection and try again."); }
    }

    /// <summary>
    /// Proposed groupings over tickets not yet in a journey. These are suggestions only — nothing is
    /// applied until the user accepts one, which is a <see cref="CreateAsync"/> with the returned ids.
    /// </summary>
    public async Task<OperationResult<List<JourneySuggestionResponse>>> GetSuggestionsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("journeys/suggestions", ct);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<JourneySuggestionResponse>>(JsonOptions, ct);
                return OperationResult<List<JourneySuggestionResponse>>.Ok(data ?? []);
            }

            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult<List<JourneySuggestionResponse>>.Fail(problem ?? "Could not load suggestions");
        }
        catch (Exception ex) { Trace.WriteLine($"[JourneyService] {ex}"); return OperationResult<List<JourneySuggestionResponse>>.Fail("Something went wrong. Check your connection and try again."); }
    }

    public async Task<OperationResult<JourneyResponse>> CreateAsync(CreateJourneyRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("journeys", request, JsonOptions, ct);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<JourneyResponse>(JsonOptions, ct);
                return OperationResult<JourneyResponse>.Ok(data!);
            }

            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult<JourneyResponse>.Fail(problem ?? "Could not create journey");
        }
        catch (Exception ex) { Trace.WriteLine($"[JourneyService] {ex}"); return OperationResult<JourneyResponse>.Fail("Something went wrong. Check your connection and try again."); }
    }

    /// <summary>
    /// Renames a journey, edits its notes, or cancels it. Note the API accepts <b>only</b>
    /// <see cref="JourneyStatus.Cancelled"/> as a status — Planned/Active/Completed are recomputed
    /// from the legs by the server's expiry sweep, so sending one of those is rejected with a 400.
    /// </summary>
    public async Task<OperationResult<JourneyResponse>> UpdateAsync(int id, UpdateJourneyRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PatchAsJsonAsync($"journeys/{id}", request, JsonOptions, ct);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<JourneyResponse>(JsonOptions, ct);
                return OperationResult<JourneyResponse>.Ok(data!);
            }

            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult<JourneyResponse>.Fail(problem ?? "Could not update journey");
        }
        catch (Exception ex) { Trace.WriteLine($"[JourneyService] {ex}"); return OperationResult<JourneyResponse>.Fail("Something went wrong. Check your connection and try again."); }
    }

    public async Task<OperationResult<JourneyResponse>> AddTicketsAsync(int id, JourneyMembershipRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"journeys/{id}/tickets", request, JsonOptions, ct);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<JourneyResponse>(JsonOptions, ct);
                return OperationResult<JourneyResponse>.Ok(data!);
            }

            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult<JourneyResponse>.Fail(problem ?? "Could not add tickets to the journey");
        }
        catch (Exception ex) { Trace.WriteLine($"[JourneyService] {ex}"); return OperationResult<JourneyResponse>.Fail("Something went wrong. Check your connection and try again."); }
    }

    /// <summary>
    /// Removes tickets from a journey.
    /// <para>
    /// Built by hand rather than with <c>DeleteAsync</c> because the endpoint takes the ticket ids
    /// in the <b>body</b>, and the convenience overload cannot send content on a DELETE.
    /// </para>
    /// </summary>
    public async Task<OperationResult<JourneyResponse>> RemoveTicketsAsync(int id, JourneyMembershipRequest request, CancellationToken ct = default)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Delete, $"journeys/{id}/tickets")
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };

            var response = await _http.SendAsync(message, ct);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<JourneyResponse>(JsonOptions, ct);
                return OperationResult<JourneyResponse>.Ok(data!);
            }

            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult<JourneyResponse>.Fail(problem ?? "Could not remove tickets from the journey");
        }
        catch (Exception ex) { Trace.WriteLine($"[JourneyService] {ex}"); return OperationResult<JourneyResponse>.Fail("Something went wrong. Check your connection and try again."); }
    }

    /// <summary>
    /// Deletes the journey. The tickets in it are <b>released, never deleted</b> — the server nulls
    /// their JourneyId — so this is a safe action to offer without a scary confirmation about losing
    /// tickets.
    /// </summary>
    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.DeleteAsync($"journeys/{id}", ct);
            if (response.IsSuccessStatusCode)
                return OperationResult.Ok();

            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult.Fail(problem ?? "Could not delete journey");
        }
        catch (Exception ex) { Trace.WriteLine($"[JourneyService] {ex}"); return OperationResult.Fail("Something went wrong. Check your connection and try again."); }
    }
}
