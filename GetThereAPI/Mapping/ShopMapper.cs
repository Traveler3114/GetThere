using GetThereShared.Contracts;

namespace GetThereAPI.Mapping;

public static class ShopMapper
{
    public static TicketableOperatorResponse ToTicketableResponse(
        int id, string name, string type, string color, string description,
        string city, string country, bool isMock, string? logoUrl) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        Color = color,
        Description = description,
        City = city,
        Country = country,
        IsMock = isMock,
        LogoUrl = logoUrl,
    };

    public static TicketPurchaseResponse ToPurchaseResponse(
        string ticketId, string operatorName, string ticketName,
        decimal price, string validFrom, string validUntil,
        string qrCodeData, bool isMock) => new()
    {
        TicketId = ticketId,
        OperatorName = operatorName,
        TicketName = ticketName,
        Price = price,
        ValidFrom = validFrom,
        ValidUntil = validUntil,
        QrCodeData = qrCodeData,
        IsMock = isMock,
    };
}
