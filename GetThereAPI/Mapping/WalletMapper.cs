using GetThereAPI.Entities;
using GetThereShared.Contracts;

namespace GetThereAPI.Mapping;

public static class WalletMapper
{
    public static WalletResponse ToResponse(Wallet entity) => new()
    {
        Id = entity.Id,
        Balance = entity.Balance,
        LastUpdated = entity.LastUpdated,
    };

    public static WalletTransactionResponse ToResponse(WalletTransaction entity) => new()
    {
        Id = entity.Id,
        Type = entity.Type,
        Amount = entity.Amount,
        Timestamp = entity.Timestamp,
        Description = entity.Description,
        WalletId = entity.WalletId,
        TicketId = entity.TicketId,
    };
}
