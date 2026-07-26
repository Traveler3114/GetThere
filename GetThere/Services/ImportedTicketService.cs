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
            var problem = await TryReadProblemAsync(response);
            return OperationResult<PagedResult<ImportedTicketResponse>>.Fail(problem ?? "Could not load imported tickets");
        }
        catch (Exception ex) { return OperationResult<PagedResult<ImportedTicketResponse>>.Fail(ex.Message); }
    }

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
            return OperationResult<ImportedTicketResponse>.Fail(problem ?? "Could not create imported ticket");
        }
        catch (Exception ex) { return OperationResult<ImportedTicketResponse>.Fail(ex.Message); }
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
