namespace NativeSpy.Agent;

public interface IClrInvocationScheduler
{
    Task<ClrInvocationResult> InvokeAsync(
        object target,
        Func<object?> callback,
        CancellationToken cancellationToken);
}

public sealed class ClrInvocationResult
{
    private ClrInvocationResult(
        object? value,
        Exception? exception,
        NativeSpy.Protocol.Common.OperationErrorDto? error)
    {
        Value = value;
        Exception = exception;
        OperationError = error;
    }

    public object? Value { get; }

    public Exception? Exception { get; }

    public NativeSpy.Protocol.Common.OperationErrorDto? OperationError { get; }

    public bool IsSuccess => OperationError is null && Exception is null;

    public static ClrInvocationResult Success(object? value)
    {
        return new ClrInvocationResult(value, null, null);
    }

    public static ClrInvocationResult Failed(Exception exception)
    {
        return new ClrInvocationResult(
            null,
            exception ?? throw new ArgumentNullException(nameof(exception)),
            null);
    }

    public static ClrInvocationResult FromError(NativeSpy.Protocol.Common.OperationErrorDto error)
    {
        return new ClrInvocationResult(
            null,
            null,
            error ?? throw new ArgumentNullException(nameof(error)));
    }
}
