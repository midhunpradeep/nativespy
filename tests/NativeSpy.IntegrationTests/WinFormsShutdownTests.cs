using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;
using NativeSpy.Agent;
using NativeSpy.Agent.Host;
using NativeSpy.Agent.Host.WinForms;
using NativeSpy.Client.NamedPipes;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Json;
using NativeSpy.Transport.NamedPipes;
using Xunit;

namespace NativeSpy.IntegrationTests;

public sealed class WinFormsShutdownTests
{
    [Fact]
    public async Task Queued_dispatch_is_settled_as_unavailable_when_dispatcher_closes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var loop = await StaWinFormsLoop.StartAsync();
        await using var dispatcher = new WinFormsUiDispatcher(loop.Anchor);
        var blockerStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockerRelease = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var targetRan = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = loop.Post(() =>
        {
            blockerStarted.TrySetResult(null);
            blockerRelease.Task.GetAwaiter().GetResult();
        });

        try
        {
            await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var context = new AgentRequestContext(1, "1", "session", 1, 10_000);
            var dispatch = dispatcher.InvokeAsync(
                () =>
                {
                    targetRan.TrySetResult(null);
                    return 42;
                },
                context,
                CancellationToken.None);

            Assert.False(dispatch.IsCompleted);
            Assert.False(targetRan.Task.IsCompleted);

            await dispatcher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            var exception = await Assert.ThrowsAsync<AgentOperationDispatchException>(
                () => dispatch);
            Assert.Equal(OperationErrorCode.ExecutionContextUnavailable, exception.Error.Code);
            Assert.False(targetRan.Task.IsCompleted);
        }
        finally
        {
            blockerRelease.TrySetResult(null);
            await blocker.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var drained = loop.Post(() => { });
        await drained.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(targetRan.Task.IsCompleted);
    }

    [Fact]
    public async Task Already_started_callback_finishes_naturally_after_host_shutdown()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var loop = await StaWinFormsLoop.StartAsync();
        var handler = new UiGateHandler(loop.Anchor, holdTarget: true);
        using var options = CreateOptions(defaultBudgetMs: 5_000, maxBudgetMs: 5_000);
        await using var host = new AgentHost(options, new UiGateCompositionFactory(handler));
        await host.StartAsync();
        await using var client = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            options.TargetProcessIdentity,
            cancellationToken: CancellationToken.None);
        using var payload = JsonDocument.Parse("{}");

        var request = client.SendRequestAsync(
            "test.ui",
            payload.RootElement,
            budgetMs: 25,
            cancellationToken: CancellationToken.None);
        await handler.TargetStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            var timeout = await request.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ProtocolJsonCodec.OperationErrorStatus, timeout.ResultStatus);
            Assert.Equal(nameof(OperationErrorCode.TargetTimeout), timeout.OperationError?.Code);

            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(handler.TargetFinished.Task.IsCompleted);
        }
        finally
        {
            handler.ReleaseTarget();
            await handler.TargetFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await handler.DispatchSettled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Queued_dispatch_shutdown_settles_handler_and_host_without_target_execution()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var loop = await StaWinFormsLoop.StartAsync();
        var blockerStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockerRelease = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = loop.Post(() =>
        {
            blockerStarted.TrySetResult(null);
            blockerRelease.Task.GetAwaiter().GetResult();
        });
        var handler = new UiGateHandler(loop.Anchor, holdTarget: false);
        using var options = CreateOptions(defaultBudgetMs: 10_000, maxBudgetMs: 10_000);
        await using var host = new AgentHost(options, new UiGateCompositionFactory(handler));
        await host.StartAsync();
        await using var client = await NamedPipeClientSession.ConnectAsync(
            host.BootstrapDescriptor,
            options.TargetProcessIdentity,
            cancellationToken: CancellationToken.None);
        using var payload = JsonDocument.Parse("{}");

        try
        {
            await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var request = client.SendRequestAsync(
                "test.ui",
                payload.RootElement,
                budgetMs: 10_000,
                cancellationToken: CancellationToken.None);
            await handler.DispatchQueued.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(handler.TargetStarted.Task.IsCompleted);

            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await handler.DispatchSettled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            var dispatchError = await handler.DispatchError.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(OperationErrorCode.ExecutionContextUnavailable, dispatchError.Code);
            Assert.False(handler.TargetStarted.Task.IsCompleted);

            _ = await Record.ExceptionAsync(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            blockerRelease.TrySetResult(null);
            await blocker.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var drained = loop.Post(() => { });
        await drained.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(handler.TargetStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task Inline_success_and_exception_release_callback_gate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var loop = await StaWinFormsLoop.StartAsync();
        await using var dispatcher = new WinFormsUiDispatcher(loop.Anchor);
        var completed = loop.Post(() =>
        {
            var first = dispatcher.InvokeAsync(
                () => 1,
                new AgentRequestContext(1, "1", "session", 1, 10_000),
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Assert.Equal(1, first);

            Assert.Throws<InvalidOperationException>(() => dispatcher.InvokeAsync<int>(
                    () => throw new InvalidOperationException("callback failed"),
                    new AgentRequestContext(2, "2", "session", 1, 10_000),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult());

            var next = dispatcher.InvokeAsync(
                () => 3,
                new AgentRequestContext(3, "3", "session", 1, 10_000),
                CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Assert.Equal(3, next);
        });

        await completed.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Queued_deadline_expiration_is_target_timeout_and_releases_callback_gate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var loop = await StaWinFormsLoop.StartAsync();
        await using var dispatcher = new WinFormsUiDispatcher(loop.Anchor);
        var blockerStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockerRelease = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = loop.Post(() =>
        {
            blockerStarted.TrySetResult(null);
            blockerRelease.Task.GetAwaiter().GetResult();
        });

        try
        {
            await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var expiredContext = new AgentRequestContext(1, "1", "session", 1, 1);
            var expired = dispatcher.InvokeAsync(
                () => 1,
                expiredContext,
                CancellationToken.None);
            SpinWait.SpinUntil(() => expiredContext.IsExpired, TimeSpan.FromSeconds(2));
            Assert.True(expiredContext.IsExpired);
            blockerRelease.TrySetResult(null);
            await blocker.WaitAsync(TimeSpan.FromSeconds(5));

            var exception = await Assert.ThrowsAsync<AgentOperationDispatchException>(
                () => expired);
            Assert.Equal(OperationErrorCode.TargetTimeout, exception.Error.Code);

            var next = dispatcher.InvokeAsync(
                () => 2,
                new AgentRequestContext(2, "2", "session", 1, 10_000),
                CancellationToken.None);
            Assert.Equal(2, await next.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            blockerRelease.TrySetResult(null);
            await blocker.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Queued_cancellation_releases_callback_gate_for_the_next_callback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var loop = await StaWinFormsLoop.StartAsync();
        await using var dispatcher = new WinFormsUiDispatcher(loop.Anchor);
        var blockerStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockerRelease = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = loop.Post(() =>
        {
            blockerStarted.TrySetResult(null);
            blockerRelease.Task.GetAwaiter().GetResult();
        });
        using var cancellation = new CancellationTokenSource();

        try
        {
            await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var context = new AgentRequestContext(1, "1", "session", 1, 10_000);
            var targetRan = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelledDispatch = dispatcher.InvokeAsync(
                () =>
                {
                    targetRan.TrySetResult(null);
                    return 1;
                },
                context,
                cancellation.Token);
            cancellation.Cancel();
            blockerRelease.TrySetResult(null);
            await blocker.WaitAsync(TimeSpan.FromSeconds(5));

            var exception = await Assert.ThrowsAsync<AgentOperationDispatchException>(
                () => cancelledDispatch);
            Assert.Equal(OperationErrorCode.ExecutionContextUnavailable, exception.Error.Code);
            Assert.False(targetRan.Task.IsCompleted);

            var next = dispatcher.InvokeAsync(
                () => 2,
                new AgentRequestContext(2, "2", "session", 1, 10_000),
                CancellationToken.None);
            Assert.Equal(2, await next.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            blockerRelease.TrySetResult(null);
            await blocker.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Queued_callback_exception_releases_callback_gate_for_the_next_callback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var loop = await StaWinFormsLoop.StartAsync();
        await using var dispatcher = new WinFormsUiDispatcher(loop.Anchor);
        var failed = dispatcher.InvokeAsync<int>(
            () => throw new InvalidOperationException("callback failed"),
            new AgentRequestContext(1, "1", "session", 1, 10_000),
            CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);

        var next = dispatcher.InvokeAsync(
            () => 2,
            new AgentRequestContext(2, "2", "session", 1, 10_000),
            CancellationToken.None);
        Assert.Equal(2, await next.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static AgentHostOptions CreateOptions(ulong defaultBudgetMs, ulong maxBudgetMs)
    {
        return new AgentHostOptions(
            "nativespy-shutdown-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            RandomNumberGenerator.GetBytes(32),
            ProcessIdentityReader.ReadCurrent(),
            NamedPipeSecurity.GetCurrentUserSid(),
            bootstrapLifetime: TimeSpan.FromSeconds(30),
            defaultBudgetMs: defaultBudgetMs,
            maxBudgetMs: maxBudgetMs);
    }

    private sealed class UiGateCompositionFactory : IAgentHostCompositionFactory
    {
        private readonly UiGateHandler _handler;

        public UiGateCompositionFactory(UiGateHandler handler)
        {
            _handler = handler;
        }

        public IReadOnlyList<string> DeclaredOperationNames { get; } = new[] { "test.ui" };

        public IAgentHostComposition Create(
            IManagedObjectReferenceService identityService,
            HostSessionContext context)
        {
            return new UiGateComposition(_handler);
        }
    }

    private sealed class UiGateComposition : IAgentHostComposition
    {
        private readonly WinFormsUiDispatcher _dispatcher;

        public UiGateComposition(UiGateHandler handler)
        {
            _dispatcher = handler.Dispatcher;
            Handlers = new[] { handler };
        }

        public IReadOnlyList<IAgentOperationHandler> Handlers { get; }

        public ValueTask DisposeAsync() => _dispatcher.DisposeAsync();
    }

    private sealed class UiGateHandler : IAgentOperationHandler
    {
        private readonly bool _holdTarget;
        private readonly TaskCompletionSource<object?> _targetRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public UiGateHandler(Control anchor, bool holdTarget)
        {
            Dispatcher = new WinFormsUiDispatcher(anchor);
            _holdTarget = holdTarget;
        }

        public WinFormsUiDispatcher Dispatcher { get; }

        public string OperationName => "test.ui";

        public TaskCompletionSource<object?> DispatchQueued { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<OperationErrorDto> DispatchError { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> DispatchSettled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> TargetStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> TargetFinished { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AgentHandlerResult> HandleAsync(
            AgentRequestContext context,
            JsonElement payload,
            CancellationToken cancellationToken)
        {
            DispatchQueued.TrySetResult(null);
            try
            {
                await Dispatcher.InvokeAsync(
                        () =>
                        {
                            TargetStarted.TrySetResult(null);
                            if (_holdTarget)
                            {
                                _targetRelease.Task.GetAwaiter().GetResult();
                            }

                            TargetFinished.TrySetResult(null);
                            return 1;
                        },
                        context,
                        cancellationToken)
                    .ConfigureAwait(false);
                return AgentHandlerResult.Success(payload);
            }
            catch (AgentOperationDispatchException exception)
            {
                DispatchError.TrySetResult(exception.Error);
                throw;
            }
            finally
            {
                DispatchSettled.TrySetResult(null);
            }
        }

        public void ReleaseTarget()
        {
            _targetRelease.TrySetResult(null);
        }
    }

    private sealed class StaWinFormsLoop : IAsyncDisposable
    {
        private readonly TaskCompletionSource<Control> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> _stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread _thread;
        private ApplicationContext? _context;
        private int _stopRequested;

        private StaWinFormsLoop()
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "NativeSpy.WinFormsTestLoop"
            };
            _thread.SetApartmentState(ApartmentState.STA);
        }

        public Control Anchor => _ready.Task.GetAwaiter().GetResult();

        public static async Task<StaWinFormsLoop> StartAsync()
        {
            var loop = new StaWinFormsLoop();
            loop._thread.Start();
            await loop._ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return loop;
        }

        public Task Post(Action action)
        {
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                Anchor.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        action();
                        completion.TrySetResult(null);
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                }));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }

            return completion.Task;
        }

        public async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
            {
                await Post(() => _context!.ExitThread()).WaitAsync(TimeSpan.FromSeconds(5));
            }

            await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }

        private void Run()
        {
            try
            {
                using var form = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    Location = new Point(-10_000, -10_000),
                    Opacity = 0,
                    ShowInTaskbar = false,
                    Size = new Size(1, 1),
                    StartPosition = FormStartPosition.Manual
                };
                form.Show();
                _context = new ApplicationContext(form);
                _ready.TrySetResult(form);
                Application.Run(_context);
            }
            catch (Exception exception)
            {
                _ready.TrySetException(exception);
                _stopped.TrySetException(exception);
            }
            finally
            {
                _stopped.TrySetResult(null);
            }
        }
    }
}
