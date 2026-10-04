using NativeSpy.Protocol.Common;

namespace NativeSpy.Agent;

public sealed class ClrInspectionResult<T>
    where T : class
{
    private ClrInspectionResult(T? value, OperationErrorDto? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public OperationErrorDto? Error { get; }

    public bool IsSuccess => Value is not null;

    public static ClrInspectionResult<T> Success(T value)
    {
        return new ClrInspectionResult<T>(
            value ?? throw new ArgumentNullException(nameof(value)),
            null);
    }

    public static ClrInspectionResult<T> Failure(OperationErrorDto error)
    {
        return new ClrInspectionResult<T>(
            null,
            error ?? throw new ArgumentNullException(nameof(error)));
    }
}
