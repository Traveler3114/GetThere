using TransitInfoAPI.Models;

namespace TransitInfoAPI.Common;

public class OperationResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }

    public static OperationResult Ok(string? message = null) =>
        new() { Success = true, Message = message };

    public static OperationResult Fail(string? errorCode, string? message = null) =>
        new() { Success = false, ErrorCode = errorCode, Message = message };

    public static OperationResult Fail(string message) =>
        new() { Success = false, Message = message };
}

public class OperationResult<T> : OperationResult
{
    public T? Data { get; set; }
    public PaginationMeta? Meta { get; set; }

    public static OperationResult<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static OperationResult<T> OkPaginated(T data, int after, int totalCount, string? nextUrl, string? message = null) =>
        new() { Success = true, Data = data, Message = message, Meta = new PaginationMeta { After = after, Next = nextUrl, TotalCount = totalCount } };

    public static new OperationResult<T> Fail(string? errorCode, string? message = null) =>
        new() { Success = false, ErrorCode = errorCode, Message = message };

    public static new OperationResult<T> Fail(string message) =>
        new() { Success = false, Message = message };
}
