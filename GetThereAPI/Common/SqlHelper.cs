using Microsoft.Data.SqlClient;

namespace GetThereAPI.Common;

public static class SqlHelper
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueConstraintViolationAlt = 2601;
    private const int ForeignKeyViolation = 547;
    private const int Deadlock = 1205;

    public static bool IsUniqueConstraintViolation(Exception ex)
        => ex is SqlException sqlEx && sqlEx.Number is UniqueConstraintViolation or UniqueConstraintViolationAlt;

    public static bool IsDeadlock(Exception ex)
        => ex is SqlException sqlEx && sqlEx.Number == Deadlock;

    public static string GetUserFriendlyMessage(SqlException sqlEx)
    {
        return sqlEx.Number switch
        {
            UniqueConstraintViolation or UniqueConstraintViolationAlt
                => "A record with the same unique identifier already exists.",
            ForeignKeyViolation
                => "This record is referenced by other data and cannot be deleted.",
            Deadlock
                => "The operation was interrupted due to a database deadlock. Please try again.",
            _ => $"A database error occurred ({sqlEx.Number})."
        };
    }
}
