namespace GetThereShared.Common;

/// <summary>
/// Standard API response wrapper.
/// Every controller returns this so the app always knows
/// whether a call succeeded and why it failed.
/// </summary>
public class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    // Parameterless constructor required for JSON deserialization
    public OperationResult() { }

    protected OperationResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public static OperationResult Ok(string message = "") => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}

public class OperationResult<T> : OperationResult
{
    public T? Data { get; set; }

    // Parameterless constructor required for JSON deserialization
    public OperationResult() { }

    protected OperationResult(bool success, string message, T? data = default)
        : base(success, message)
    {
        Data = data;
    }

    public static OperationResult<T> Ok(T data, string message = "") => new(true, message, data);
    public static new OperationResult<T> Fail(string message) => new(false, message);
}