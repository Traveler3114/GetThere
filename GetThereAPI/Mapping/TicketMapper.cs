using GetThereAPI.Entities;
using GetThereShared.Contracts;

namespace GetThereAPI.Mapping;

public static class TicketMapper
{
    public static TicketOptionResponse ToOptionResponse(TicketOption to) => new()
    {
        Id = to.Id,
        AdapterId = to.TicketingAdapterId,
        AdapterName = to.Adapter.Name,
        ExternalProductId = to.ExternalProductId,
        Name = to.Name,
        Description = to.Description,
        Price = to.Price,
        Currency = to.Currency,
        TicketFormat = to.TicketFormat,
        DurationMinutes = to.DurationMinutes
    };

    public static TicketResponse ToTicketResponse(Ticket t) => new()
    {
        Id = t.Id,
        PurchaseId = t.PurchaseId,
        ExternalTicketId = t.ExternalTicketId,
        Format = t.Format,
        Data = t.Data,
        ValidFrom = t.ValidFrom,
        ValidTo = t.ValidTo,
        Status = t.Status,
        Option = new TicketOptionResponse
        {
            Id = t.Purchase.TicketOption.Id,
            AdapterId = t.Purchase.TicketingAdapterId,
            AdapterName = t.Purchase.Adapter.Name,
            ExternalProductId = t.Purchase.TicketOption.ExternalProductId,
            Name = t.Purchase.TicketOption.Name,
            Price = t.Purchase.TicketOption.Price,
            Currency = t.Purchase.TicketOption.Currency,
            TicketFormat = t.Purchase.TicketOption.TicketFormat,
            DurationMinutes = t.Purchase.TicketOption.DurationMinutes
        }
    };
}
