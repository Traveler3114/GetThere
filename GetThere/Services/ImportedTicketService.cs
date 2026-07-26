using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using GetThereShared.Common;
using GetThereShared.Contracts;
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

    public async Task<OperationResult<List<ImportedTicketResponse>>> ListAsync()
    {
        try
        {
            var response = await _http.GetAsync("importedtickets");
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<List<ImportedTicketResponse>>(JsonOptions);
                return OperationResult<List<ImportedTicketResponse>>.Ok(data ?? []);
            }
            var problem = await TryReadProblemAsync(response);
            return OperationResult<List<ImportedTicketResponse>>.Fail(problem ?? "Could not load imported tickets");
        }
        catch (Exception ex) { return OperationResult<List<ImportedTicketResponse>>.Fail(ex.Message); }
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
            return response.IsSuccessStatusCode
                ? OperationResult.Ok()
                : OperationResult.Fail("Could not cancel ticket");
        }
        catch (Exception ex) { return OperationResult.Fail(ex.Message); }
    }
}
