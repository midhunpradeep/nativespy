using System.Windows.Forms;
using NativeSpy.Agent.Host;
using NativeSpy.Protocol.Common;

namespace NativeSpy.Agent.Host.WinForms;

internal sealed class WinFormsUiDispatcher : IWinFormsTargetDispatcher
{
    private readonly Control _anchor;
    private readonly SemaphoreSlim _callbackGate = new(1, 1);
    private int _disposed;

    public WinFormsUiDispatcher(Control anchor)
    {
        _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
    }

    public Task<T> InvokeAsync<T>(
        Func<T> callback,
        AgentRequestContext context,
        CancellationToken cancellationToken)
    {
        if (callback is null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new AgentOperationDispatchException(
                CreateUnavailableError(context, "The WinForms execution context is closed."));
        }

        try
        {
            _callbackGate.Wait(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new AgentOperationDispatchException(
                context.IsExpired
                    ? new OperationErrorDto(
                        OperationErrorCode.TargetTimeout,
                        "The operation budget expired while waiting for the UI callback slot.")
                    : CreateUnavailableError(
                        context,
                        "The WinForms execution context was cancelled."));
        }

        try
        {
            return InvokeWithCallbackSlot(callback, context, cancellationToken);
        }
        catch
        {
            _callbackGate.Release();
            throw;
        }
    }

    private Task<T> InvokeWithCallbackSlot<T>(
        Func<T> callback,
        AgentRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.IsExpired || cancellationToken.IsCancellationRequested)
        {
            throw new AgentOperationDispatchException(
                context.IsExpired
                    ? new OperationErrorDto(
                        OperationErrorCode.TargetTimeout,
                        "The operation budget expired before UI dispatch.")
                    : CreateUnavailableError(
                        context,
                        "The WinForms execution context was cancelled."));
        }

        if (!_anchor.IsHandleCreated || _anchor.IsDisposed || _anchor.Disposing)
        {
            throw new AgentOperationDispatchException(
                new OperationErrorDto(
                    OperationErrorCode.ExecutionContextUnavailable,
                    "The WinForms dispatch anchor is unavailable."));
        }

        if (!_anchor.InvokeRequired)
        {
            try
            {
                return Task.FromResult(InvokeOnUiThread(callback, context, cancellationToken));
            }
            finally
            {
                _callbackGate.Release();
            }
        }

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _anchor.BeginInvoke(new Action(() =>
            {
                if (Volatile.Read(ref _disposed) != 0
                    || cancellationToken.IsCancellationRequested
                    || context.IsExpired)
                {
                    completion.TrySetException(new AgentOperationDispatchException(
                        context.IsExpired
                            ? new OperationErrorDto(
                                OperationErrorCode.TargetTimeout,
                                "The operation budget expired before UI work started.")
                            : CreateUnavailableError(
                                context,
                                "The WinForms execution context was cancelled.")));
                    return;
                }

                try
                {
                    completion.TrySetResult(InvokeOnUiThread(callback, context, cancellationToken));
                }
                catch (AgentOperationDispatchException exception)
                {
                    completion.TrySetException(exception);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }));
        }
        catch (Exception exception)
        {
            throw new AgentOperationDispatchException(
                new OperationErrorDto(
                    OperationErrorCode.ExecutionContextUnavailable,
                    exception.Message));
        }

        _ = completion.Task.ContinueWith(
            _ => _callbackGate.Release(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return completion.Task;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }

    private static T InvokeOnUiThread<T>(
        Func<T> callback,
        AgentRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.IsExpired || cancellationToken.IsCancellationRequested)
        {
            throw new AgentOperationDispatchException(
                context.IsExpired
                    ? new OperationErrorDto(
                        OperationErrorCode.TargetTimeout,
                        "The operation budget expired before UI work started.")
                    : CreateUnavailableError(
                        context,
                        "The WinForms execution context was cancelled."));
        }

        return callback();
    }

    private static OperationErrorDto CreateUnavailableError(
        AgentRequestContext context,
        string message)
    {
        return new OperationErrorDto(
            context.IsExpired
                ? OperationErrorCode.TargetTimeout
                : OperationErrorCode.ExecutionContextUnavailable,
            context.IsExpired
                ? "The operation budget expired before UI work started."
                : message);
    }
}
