using GetThereAPI.Models;

namespace GetThereAPI.Sdk;

public interface ITicketingAdapter
{
    string Name { get; }
    string AdapterType { get; }
    List<RequiredInput> RequiredInputs { get; }

    Task<PurchaseResult> PurchaseAsync(PurchaseRequest request, CancellationToken ct = default);
    Task<TicketPayload?> ValidateAsync(string externalTicketId, CancellationToken ct = default);
}
