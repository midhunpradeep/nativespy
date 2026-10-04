using NativeSpy.Agent;
using NativeSpy.Protocol.Common;

namespace NativeSpy.Agent.Host;

public enum ClrExecutionContextKind
{
    Direct,
    Adapter,
    Unavailable,
    Conflict
}

public sealed class ClrExecutionContextResolution
{
    private ClrExecutionContextResolution(
        ClrExecutionContextKind kind,
        IClrExecutionContextAdapter? adapter,
        OperationErrorDto? error)
    {
        Kind = kind;
        Adapter = adapter;
        Error = error;
    }

    public ClrExecutionContextKind Kind { get; }

    public IClrExecutionContextAdapter? Adapter { get; }

    public OperationErrorDto? Error { get; }

    public static ClrExecutionContextResolution Direct()
    {
        return new ClrExecutionContextResolution(ClrExecutionContextKind.Direct, null, null);
    }

    public static ClrExecutionContextResolution AdapterAvailable(IClrExecutionContextAdapter adapter)
    {
        return new ClrExecutionContextResolution(
            ClrExecutionContextKind.Adapter,
            adapter ?? throw new ArgumentNullException(nameof(adapter)),
            null);
    }

    public static ClrExecutionContextResolution Unavailable(OperationErrorDto error)
    {
        return new ClrExecutionContextResolution(
            ClrExecutionContextKind.Unavailable,
            null,
            error ?? throw new ArgumentNullException(nameof(error)));
    }

    public static ClrExecutionContextResolution Conflict(OperationErrorDto error)
    {
        return new ClrExecutionContextResolution(
            ClrExecutionContextKind.Conflict,
            null,
            error ?? throw new ArgumentNullException(nameof(error)));
    }
}

public interface IClrExecutionContextAdapter
{
    bool Requires(object target);

    Task<object?> InvokeAsync(
        Func<object?> callback,
        AgentRequestContext context,
        CancellationToken cancellationToken);
}

public interface IClrExecutionContextResolver
{
    ClrExecutionContextResolution Resolve(object target);
}

public sealed class CompositeClrExecutionContextResolver : IClrExecutionContextResolver
{
    private readonly IReadOnlyList<IClrExecutionContextAdapter> _adapters;

    public CompositeClrExecutionContextResolver(IEnumerable<IClrExecutionContextAdapter> adapters)
    {
        _adapters = (adapters ?? throw new ArgumentNullException(nameof(adapters))).ToArray();
    }

    public ClrExecutionContextResolution Resolve(object target)
    {
        var matches = _adapters.Where(adapter => adapter.Requires(target)).ToArray();
        if (matches.Length == 0)
        {
            return ClrExecutionContextResolution.Direct();
        }

        if (matches.Length > 1)
        {
            return ClrExecutionContextResolution.Conflict(
                new OperationErrorDto(
                    OperationErrorCode.ExecutionContextConflict,
                    "Multiple execution contexts claimed the target."));
        }

        return ClrExecutionContextResolution.AdapterAvailable(matches[0]);
    }
}

public sealed class ClrInvocationScheduler : IClrInvocationScheduler
{
    private readonly IClrExecutionContextResolver _resolver;
    private readonly AgentRequestContext _context;

    public ClrInvocationScheduler(
        IClrExecutionContextResolver resolver,
        AgentRequestContext context)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ClrInvocationResult> InvokeAsync(
        object target,
        Func<object?> callback,
        CancellationToken cancellationToken)
    {
        if (_context.IsExpired)
        {
            return ClrInvocationResult.FromError(
                new OperationErrorDto(
                    OperationErrorCode.TargetTimeout,
                    "The operation budget expired before the getter started."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ClrInvocationResult.FromError(
                new OperationErrorDto(
                    OperationErrorCode.ExecutionContextUnavailable,
                    "The execution context was cancelled."));
        }

        var resolution = _resolver.Resolve(target);
        if (resolution.Kind is ClrExecutionContextKind.Unavailable or ClrExecutionContextKind.Conflict)
        {
            return ClrInvocationResult.FromError(resolution.Error!);
        }

        try
        {
            if (resolution.Kind == ClrExecutionContextKind.Adapter)
            {
                var value = await resolution.Adapter!
                    .InvokeAsync(callback, _context, cancellationToken)
                    .ConfigureAwait(false);
                return ClrInvocationResult.Success(value);
            }

            return ClrInvocationResult.Success(callback());
        }
        catch (AgentOperationDispatchException exception)
        {
            return ClrInvocationResult.FromError(exception.Error);
        }
        catch (Exception exception) when (IsTargetException(exception))
        {
            return ClrInvocationResult.Failed(exception);
        }
    }

    private static bool IsTargetException(Exception exception)
    {
        return exception is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException
            and not ThreadAbortException;
    }
}
