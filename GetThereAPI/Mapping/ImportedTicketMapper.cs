using System.Linq.Expressions;

using GetThereAPI.Entities;

using GetThereShared.Contracts;

namespace GetThereAPI.Mapping;

public static class ImportedTicketMapper
{
    public static Expression<Func<ImportedTicket, ImportedTicketResponse>> ToResponseExpression =>
        t => new ImportedTicketResponse
        {
            Id = t.Id,
            OperatorGlobalId = t.OperatorGlobalId,
            OperatorNameSnapshot = t.OperatorNameSnapshot,
            Source = t.Source,
            Status = t.Status,
            Verification = t.Verification,
            TicketName = t.TicketName,
            RouteDescription = t.RouteDescription,
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
        OperatorGlobalId = t.OperatorGlobalId,
        OperatorNameSnapshot = t.OperatorNameSnapshot,
        Source = t.Source,
        Status = t.Status,
        Verification = t.Verification,
        TicketName = t.TicketName,
        RouteDescription = t.RouteDescription,
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
