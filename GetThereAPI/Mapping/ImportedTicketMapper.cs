using System.Linq.Expressions;

using GetThereAPI.Entities;

using GetThereShared.Contracts;

namespace GetThereAPI.Mapping;

public static class ImportedTicketMapper
{
    /// <summary>
    /// The list projection. It omits <c>RawPayload</c> on purpose — that column holds the entire
    /// raw ticket blob, and a page of fifty would carry fifty of them. The single-ticket
    /// <see cref="ToResponse"/> includes it; the two are meant to differ in exactly that one field.
    /// </summary>
    public static Expression<Func<ImportedTicket, ImportedTicketResponse>> ToResponseExpression =>
        t => new ImportedTicketResponse
        {
            Id = t.Id,
            ClientId = t.ClientId,
            OperatorGlobalId = t.OperatorGlobalId,
            OperatorNameSnapshot = t.OperatorNameSnapshot,
            Source = t.Source,
            Status = t.Status,
            Verification = t.Verification,
            TicketName = t.TicketName,
            RouteDescription = t.RouteDescription,
            OriginName = t.OriginName,
            DestinationName = t.DestinationName,
            Price = t.Price,
            Currency = t.Currency,
            ValidFrom = t.ValidFrom,
            ValidTo = t.ValidTo,
            PayloadFormat = t.PayloadFormat,
            SourceFileBlobKey = t.SourceFileBlobKey,
            SourceFileContentType = t.SourceFileContentType,
            JourneyId = t.JourneyId,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        };

    public static ImportedTicketResponse ToResponse(ImportedTicket t) => new()
    {
        Id = t.Id,
        ClientId = t.ClientId,
        OperatorGlobalId = t.OperatorGlobalId,
        OperatorNameSnapshot = t.OperatorNameSnapshot,
        Source = t.Source,
        Status = t.Status,
        Verification = t.Verification,
        TicketName = t.TicketName,
        RouteDescription = t.RouteDescription,
        OriginName = t.OriginName,
        DestinationName = t.DestinationName,
        Price = t.Price,
        Currency = t.Currency,
        ValidFrom = t.ValidFrom,
        ValidTo = t.ValidTo,
        RawPayload = t.RawPayload,
        PayloadFormat = t.PayloadFormat,
        SourceFileBlobKey = t.SourceFileBlobKey,
        SourceFileContentType = t.SourceFileContentType,
        JourneyId = t.JourneyId,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };
}
