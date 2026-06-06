using GetThereAPI.Entities;
using GetThereShared.Contracts;

namespace GetThereAPI.Mapping;

public static class TicketMapper
{
    public static TicketResponse ToResponse(Ticket entity) => new()
    {
        Id = entity.Id,
        UserId = entity.UserId,
        TicketType = entity.TicketType,
        PurchasedAt = entity.PurchasedAt,
        ValidFrom = entity.ValidFrom,
        ValidUntil = entity.ValidUntil,
        Format = entity.Format,
        Payload = entity.Payload,
        DisplayInstructions = entity.DisplayInstructions,
        Status = entity.Status,
        TransitOperatorId = entity.TransitOperatorId,
    };
}
