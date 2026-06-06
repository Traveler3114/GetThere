using GetThereAPI.Entities;
using GetThereShared.Contracts;

namespace GetThereAPI.Mapping;

public static class PaymentMapper
{
    public static PaymentProviderResponse ToResponse(PaymentProvider entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
    };
}
