namespace TransitInfoAPI.Enums;

// Not stored in DB — used internally by engine for deserializing PaginationConfig JSON
public enum CustomFeedPaginationType
{
    Page,
    Offset,
    Cursor
}
