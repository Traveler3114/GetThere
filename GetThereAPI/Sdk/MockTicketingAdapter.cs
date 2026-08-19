using GetThereAPI.Models;

using GetThereShared.Enums;

namespace GetThereAPI.Sdk;

/// <summary>
/// A stand-in ticketing adapter for development: it "sells" a ticket by minting a fake QR payload, so
/// the purchasable-now path works end-to-end without a real operator integration or payment provider —
/// the running-app analogue of the test <c>FakeTicketingAdapter</c>. Registered only in Development.
/// </summary>
public sealed class MockTicketingAdapter : ITicketingAdapter
{
    public const string Type = "mock.v1";

    public string Name => "Mock operator";
    public string AdapterType => Type;
    public List<RequiredInput> RequiredInputs => [];
    public bool CanPurchase => true;

    public Task<PurchaseResult> PurchaseAsync(PurchaseRequest request, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return Task.FromResult(new PurchaseResult
        {
            Success = true,
            ExternalPurchaseId = "MOCK-" + Guid.NewGuid().ToString("N"),
            Ticket = new TicketPayload
            {
                Format = TicketFormat.QR,
                Data = "MOCK-TICKET-" + request.PaymentReference,
                ValidFrom = now,
                ValidTo = now.AddHours(2),
            },
        });
    }

    public Task<TicketPayload?> ValidateAsync(string externalTicketId, CancellationToken ct = default)
        => Task.FromResult<TicketPayload?>(null);
}
