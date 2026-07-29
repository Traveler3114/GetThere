using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Contracts;
using GetThereShared.Enums;

using static GetThereShared.Common.HttpHelper;

namespace GetThere.Services;

public class ImportedTicketService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public ImportedTicketService(HttpClient http) { _http = http; }

    public async Task<OperationResult<PagedResult<ImportedTicketResponse>>> ListAsync(int page = 1, int perPage = 50, ImportedTicketStatus? status = null, CancellationToken ct = default)
    {
        try
        {
            var qs = new StringBuilder($"importedtickets?page={page}&perPage={perPage}");
            if (status.HasValue)
                qs.Append($"&status={status.Value}");
            var response = await _http.GetAsync(qs.ToString(), ct);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<PagedResult<ImportedTicketResponse>>(JsonOptions, ct);
                return OperationResult<PagedResult<ImportedTicketResponse>>.Ok(data!);
            }
            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult<PagedResult<ImportedTicketResponse>>.Fail(problem ?? "Could not load imported tickets");
        }
        catch (Exception ex) { return OperationResult<PagedResult<ImportedTicketResponse>>.Fail(ex.Message); }
    }

    /// <summary>
    /// A ticket the server judged a duplicate of one already imported. Resending with
    /// <see cref="CreateImportedTicketRequest.AllowDuplicate"/> is the way through.
    /// </summary>
    public const string DuplicateCode = "DUPLICATE_TICKET";

    public async Task<OperationResult<ImportedTicketResponse>> CreateAsync(CreateImportedTicketRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("importedtickets", request, JsonOptions);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<ImportedTicketResponse>(JsonOptions);
                return OperationResult<ImportedTicketResponse>.Ok(data!);
            }

            var problem = await TryReadProblemAsync(response);

            // Taken from the status code rather than the body: the server's duplicate check answers
            // with 409 from two places — a pre-check and the filtered unique index catching a race —
            // and only the status is guaranteed to be the same for both.
            if (response.StatusCode == HttpStatusCode.Conflict)
                return OperationResult<ImportedTicketResponse>.Fail(
                    DuplicateCode, problem ?? "You have already imported this ticket.");

            return OperationResult<ImportedTicketResponse>.Fail(problem ?? "Could not create imported ticket");
        }
        catch (Exception ex) { return OperationResult<ImportedTicketResponse>.Fail(ex.Message); }
    }

    public async Task<OperationResult<ImportedTicketResponse>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"importedtickets/{id}", ct);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<ImportedTicketResponse>(JsonOptions, ct);
                return OperationResult<ImportedTicketResponse>.Ok(data!);
            }
            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult<ImportedTicketResponse>.Fail(problem ?? "Could not load ticket");
        }
        catch (Exception ex) { return OperationResult<ImportedTicketResponse>.Fail(ex.Message); }
    }

    /// <summary>
    /// Moves a ticket to a new status. Without this the API's PATCH endpoint was unreachable, so
    /// nothing could ever mark a ticket used — while the list still rendered a "Used" filter chip
    /// that was permanently empty.
    /// </summary>
    public async Task<OperationResult<ImportedTicketResponse>> UpdateStatusAsync(int id, ImportedTicketStatus status, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PatchAsJsonAsync($"importedtickets/{id}/status",
                new UpdateImportedTicketStatusRequest { Status = status }, JsonOptions, ct);

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<ImportedTicketResponse>(JsonOptions, ct);
                return OperationResult<ImportedTicketResponse>.Ok(data!);
            }
            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult<ImportedTicketResponse>.Fail(problem ?? "Could not update ticket");
        }
        catch (Exception ex) { return OperationResult<ImportedTicketResponse>.Fail(ex.Message); }
    }

    /// <summary>
    /// Uploads a ticket file and returns what could be read from it, plus the single-use blob key
    /// to pass to <see cref="CreateAsync"/>. No ticket exists until the user confirms the draft.
    /// </summary>
    public async Task<OperationResult<TicketUploadResponse>> UploadAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        try
        {
            // Buffered before sending rather than wrapped as a StreamContent over the picker's
            // stream. AuthenticatedHttpHandler rebuilds and re-sends the request after refreshing an
            // expired token, and it does that by re-reading request.Content — which a one-shot
            // stream has already given up. The upload then failed with a generic error at exactly
            // the moment the retry existed to cover. A ByteArrayContent can be read twice.
            //
            // The server caps an upload at 10 MB (TicketUploadManager.MaxFileBytes), so this is a
            // bounded buffer, not an open-ended one.
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);

            using var form = new MultipartFormDataContent();
            using var file = new ByteArrayContent(buffer.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, "file", fileName);

            var response = await _http.PostAsync("importedtickets/upload", form, ct);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<TicketUploadResponse>(JsonOptions, ct);
                return OperationResult<TicketUploadResponse>.Ok(data!);
            }
            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult<TicketUploadResponse>.Fail(problem ?? "Could not read that file");
        }
        catch (Exception ex) { return OperationResult<TicketUploadResponse>.Fail(ex.Message); }
    }

    /// <summary>Scrapes pasted confirmation text. No file, so nothing is stored.</summary>
    public async Task<OperationResult<TicketExtractionResult>> ExtractTextAsync(string text, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("importedtickets/extract-text",
                new ExtractTicketTextRequest { Text = text }, JsonOptions, ct);

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<TicketExtractionResult>(JsonOptions, ct);
                return OperationResult<TicketExtractionResult>.Ok(data!);
            }
            var problem = await TryReadProblemAsync(response, ct);
            return OperationResult<TicketExtractionResult>.Fail(problem ?? "Could not read that text");
        }
        catch (Exception ex) { return OperationResult<TicketExtractionResult>.Fail(ex.Message); }
    }

    public async Task<OperationResult> CancelAsync(int id)
    {
        try
        {
            var response = await _http.DeleteAsync($"importedtickets/{id}");
            if (response.IsSuccessStatusCode)
                return OperationResult.Ok();
            var problem = await TryReadProblemAsync(response);
            return OperationResult.Fail(problem ?? "Could not cancel ticket");
        }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }
}
