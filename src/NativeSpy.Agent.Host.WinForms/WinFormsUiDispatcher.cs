using System.Collections.Concurrent;
using System.Windows.Forms;
using NativeSpy.Agent.Host;
using NativeSpy.Protocol.Common;

namespace NativeSpy.Agent.Host.WinForms;

internal sealed class WinFormsUiDispatcher : IWinFormsTargetDispatcher
{
    private readonly Control _anchor;
    private readonly SemaphoreSlim _callbackGate = new(1, 1);
    private readonly ConcurrentDictionary<long, IPendingDispatch> _pendingDispatches = new();
    private long _nextDispatchId;
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
                CreateUnavailableError(context, "The WinForms execution context was cancelled."));
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            _callbackGate.Release();
            throw new AgentOperationDispatchException(
                CreateUnavailableError(context, "The WinForms execution context is closed."));
        }

        if (context.IsExpired || cancellationToken.IsCancellationRequested)
        {
            _callbackGate.Release();
            throw new AgentOperationDispatchException(
                CreateUnavailableError(context, "The WinForms execution context was cancelled."));
        }

        if (!_anchor.IsHandleCreated || _anchor.IsDisposed || _anchor.Disposing)
        {
            _callbackGate.Release();
            throw new AgentOperationDispatchException(
                new OperationErrorDto(
                    OperationErrorCode.ExecutionContextUnavailable,
                    "The WinForms dispatch anchor is unavailable."));
        }

        if (!_anchor.InvokeRequired)
        {
            try
            {
                EnsureCanStart(context, cancellationToken);
                return Task.FromResult(callback());
            }
            finally
            {
                _callbackGate.Release();
            }
        }

        var dispatchId = Interlocked.Increment(ref _nextDispatchId);
        var dispatch = new DispatchOperation<T>(
            context,
            () => _callbackGate.Release(),
            () => _pendingDispatches.TryRemove(dispatchId, out _));
        _pendingDispatches[dispatchId] = dispatch;

        // Dispose may have raced with registration. The post-registration check
        // closes that gap without requiring removal from the WinForms queue.
        if (Volatile.Read(ref _disposed) != 0)
        {
            SettlePendingDispatch(dispatchId, dispatch);
        }

        try
        {
            _anchor.BeginInvoke(new Action(() => ExecuteQueuedDispatch(
                dispatch,
                callback,
                context,
                cancellationToken)));
        }
        catch (Exception exception)
        {
            _pendingDispatches.TryRemove(dispatchId, out _);
            if (dispatch.TryAbortBeforeStart())
            {
                throw new AgentOperationDispatchException(
                    CreateUnavailableError(context, exception.Message));
            }

            // Dispatcher disposal won the race and already settled the task.
            return dispatch.Completion.Task;
        }

        return dispatch.Completion.Task;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        foreach (var pair in _pendingDispatches)
        {
            SettlePendingDispatch(pair.Key, pair.Value);
        }

        return ValueTask.CompletedTask;
    }

    private void ExecuteQueuedDispatch<T>(
        DispatchOperation<T> dispatch,
        Func<T> callback,
        AgentRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.IsExpired || cancellationToken.IsCancellationRequested)
        {
            if (dispatch.TrySettleBeforeStart(
                    CreateUnavailableError(
                        context,
                        "The WinForms execution context was cancelled.")))
            {
                return;
            }

            // The UI delegate won the start race. Once it owns execution,
            // shutdown must not replace the target callback's result.
            return;
        }

        if (!dispatch.TryStart())
        {
            return;
        }

        try
        {
            dispatch.SettleResult(callback());
        }
        catch (Exception exception)
        {
            dispatch.SettleException(exception);
        }
    }

    private void SettlePendingDispatch(long dispatchId, IPendingDispatch dispatch)
    {
        if (dispatch.TrySettleBeforeStart(
                CreateUnavailableError(
                    dispatch.Context,
                    "The WinForms execution context is closed.")))
        {
            _pendingDispatches.TryRemove(dispatchId, out _);
        }
    }

    private static void EnsureCanStart(
        AgentRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.IsExpired || cancellationToken.IsCancellationRequested)
        {
            throw new AgentOperationDispatchException(
                CreateUnavailableError(
                    context,
                    "The WinForms execution context was cancelled."));
        }
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

    private interface IPendingDispatch
    {
        AgentRequestContext Context { get; }

        bool TrySettleBeforeStart(OperationErrorDto error);
    }

    private sealed class DispatchOperation<T> : IPendingDispatch
    {
        private const int Queued = 0;
        private const int Started = 1;
        private const int Settled = 2;

        private readonly Action _releaseGate;
        private readonly Action _removeFromPending;
        private int _state;
        private int _gateReleased;

        public DispatchOperation(
            AgentRequestContext context,
            Action releaseGate,
            Action removeFromPending)
        {
            Context = context;
            _releaseGate = releaseGate;
            _removeFromPending = removeFromPending;
        }

        public AgentRequestContext Context { get; }

        public TaskCompletionSource<T> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryStart()
        {
            return Interlocked.CompareExchange(ref _state, Started, Queued) == Queued;
        }

        public bool TrySettleBeforeStart(OperationErrorDto error)
        {
            if (Interlocked.CompareExchange(ref _state, Settled, Queued) != Queued)
            {
                return false;
            }

            Completion.TrySetException(new AgentOperationDispatchException(error));
            FinishSettlement();
            return true;
        }

        public bool TryAbortBeforeStart()
        {
            if (Interlocked.CompareExchange(ref _state, Settled, Queued) != Queued)
            {
                return false;
            }

            FinishSettlement();
            return true;
        }

        public void SettleResult(T result)
        {
            if (Interlocked.CompareExchange(ref _state, Settled, Started) != Started)
            {
                return;
            }

            Completion.TrySetResult(result);
            FinishSettlement();
        }

        public void SettleException(Exception exception)
        {
            if (Interlocked.CompareExchange(ref _state, Settled, Started) != Started)
            {
                return;
            }

            Completion.TrySetException(exception);
            FinishSettlement();
        }

        private void FinishSettlement()
        {
            _removeFromPending();
            if (Interlocked.Exchange(ref _gateReleased, 1) == 0)
            {
                _releaseGate();
            }
        }
    }
}
