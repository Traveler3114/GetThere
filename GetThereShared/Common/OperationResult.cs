using System.Text.Json.Serialization;

namespace GetThereShared.Common;

public class OperationResult
{
    public bool Success { get; set; }
    public string? Code { get; set; }
    public string Message { get; set; } = string.Empty;

    public OperationResult() { }

    [JsonConstructor]
    protected OperationResult(bool success, string message, string? code = null)
    {
        Success = success;
        Message = message;
        Code = code;
    }

    public static OperationResult Ok(string message = "") => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
    public static OperationResult Fail(string code, string message) => new(false, message, code);
}

public class OperationResult<T> : OperationResult
{
    public T? Data { get; set; }

    public OperationResult() { }

    protected OperationResult(bool success, string message, T? data = default, string? code = null)
        : base(success, message, code)
    {
        Data = data;
    }

    public static OperationResult<T> Ok(T data, string message = "") => new(true, message, data);
    public static new OperationResult<T> Fail(string message) => new(false, message);
    public static new OperationResult<T> Fail(string code, string message) => new(false, message, code: code);
}
