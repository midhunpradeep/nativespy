using System.Collections.Concurrent;

namespace NativeSpy.FlaUI;

internal sealed class FlaUiMtaExecutor : IDisposable
{
    private readonly BlockingCollection<IWorkItem> _queue = new();
    private readonly Thread _thread;
    private readonly Action _onThreadExited;
    private readonly TaskCompletionSource<object?> _threadExited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;
    private int _poisoned;

    public FlaUiMtaExecutor(Action onThreadExited)
    {
        _onThreadExited = onThreadExited ?? throw new ArgumentNullException(nameof(onThreadExited));
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "NativeSpy.FlaUI.UIA3.MTA"
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public bool IsPoisoned => Volatile.Read(ref _poisoned) != 0;

    public Task<T> InvokeAsync<T>(Func<T> callback, CancellationToken cancellationToken)
    {
        if (callback is null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        if (Volatile.Read(ref _disposed) != 0 || IsPoisoned)
        {
            throw new FlaUiSessionException("The UI Automation executor is unavailable.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var work = new WorkItem<T>(callback);
        try
        {
            _queue.Add(work, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw new FlaUiSessionException("The UI Automation executor is closed.");
        }

        return work.Completion.Task;
    }

    public async Task<T> InvokeWithTimeoutAsync<T>(
        Func<T> callback,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var task = InvokeAsync(callback, cancellationToken);
        var completed = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
        if (!ReferenceEquals(completed, task))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Poison();
            throw new FlaUiSessionException("The UI Automation executor became unresponsive.");
        }

        return await task.ConfigureAwait(false);
    }

    public void Poison()
    {
        if (Interlocked.Exchange(ref _poisoned, 1) != 0)
        {
            return;
        }

        _queue.CompleteAdding();
        while (_queue.TryTake(out var work))
        {
            work.Fail(new FlaUiSessionException("The UI Automation executor was quarantined."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.CompleteAdding();
        if (!IsPoisoned && Thread.CurrentThread != _thread)
        {
            try
            {
                await _threadExited.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                _queue.Dispose();
            }
            catch (TimeoutException)
            {
                // A normal dispose may still encounter an unmanaged call that
                // exceeded the bounded worker lifetime. Leave the queue and
                // worker-owned UIA resources for the worker's eventual exit.
            }
        }
        // A poisoned worker may still be inside an unmanaged UIA call. Do not
        // dispose its queue or automation objects from this thread; the worker
        // owns their eventual teardown boundary.
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void Run()
    {
        try
        {
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                work.Execute();
            }
        }
        finally
        {
            try
            {
                _onThreadExited();
            }
            finally
            {
                _threadExited.TrySetResult(null);
            }
        }
    }

    private interface IWorkItem
    {
        void Execute();

        void Fail(Exception exception);
    }

    private sealed class WorkItem<T> : IWorkItem
    {
        private const int Queued = 0;
        private const int Started = 1;
        private const int Failed = 2;
        private readonly Func<T> _callback;
        private int _state;

        public WorkItem(Func<T> callback)
        {
            _callback = callback;
        }

        public TaskCompletionSource<T> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Execute()
        {
            if (Interlocked.CompareExchange(ref _state, Started, Queued) != Queued)
            {
                return;
            }

            try
            {
                Completion.TrySetResult(_callback());
            }
            catch (Exception exception)
            {
                Completion.TrySetException(exception);
            }
        }

        public void Fail(Exception exception)
        {
            if (Interlocked.CompareExchange(ref _state, Failed, Queued) == Queued)
            {
                Completion.TrySetException(exception);
            }
        }
    }
}

public sealed class FlaUiSessionException : Exception
{
    public FlaUiSessionException(string message)
        : base(message)
    {
    }
}
