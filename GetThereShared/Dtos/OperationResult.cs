public class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; }

    public OperationResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public static OperationResult Ok(string message = "") => new OperationResult(true, message);
    public static OperationResult Fail(string message) => new OperationResult(false, message);
}

public class OperationResult<T> : OperationResult
{
    public T? Data { get; set; }

    public OperationResult(bool success, string message, T? data = default) : base(success, message)
    {
        Data = data;
    }

    public static OperationResult<T> Ok(T data, string message = "") => new OperationResult<T>(true, message, data);
    public static new OperationResult<T> Fail(string message) => new OperationResult<T>(false, message);
}