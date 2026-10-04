using System.Windows.Forms;
using NativeSpy.Agent.Host;
using NativeSpy.Agent.Host.WinForms;
using NativeSpy.Protocol.Common;
using Xunit;

namespace NativeSpy.IntegrationTests;

public sealed class WinFormsUiDispatcherTests
{
    [Fact]
    public async Task Host_cancellation_is_not_reported_as_target_timeout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var anchor = new Control();
        await using var dispatcher = new WinFormsUiDispatcher(anchor);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new AgentRequestContext(1, "1", "session", 1, 10_000);

        var exception = Assert.Throws<AgentOperationDispatchException>(() =>
            dispatcher.InvokeAsync(() => 42, context, cancellation.Token)
                .GetAwaiter()
                .GetResult());

        Assert.Equal(OperationErrorCode.ExecutionContextUnavailable, exception.Error.Code);
    }

    [Fact]
    public async Task Expired_context_is_reported_as_target_timeout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var anchor = new Control();
        await using var dispatcher = new WinFormsUiDispatcher(anchor);
        var context = new AgentRequestContext(1, "1", "session", 1, 1);
        await Task.Delay(25);

        var exception = Assert.Throws<AgentOperationDispatchException>(() =>
            dispatcher.InvokeAsync(() => 42, context, CancellationToken.None)
                .GetAwaiter()
                .GetResult());

        Assert.Equal(OperationErrorCode.TargetTimeout, exception.Error.Code);
    }

    [Fact]
    public async Task Disposed_dispatcher_is_execution_context_unavailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var anchor = new Control();
        await using var dispatcher = new WinFormsUiDispatcher(anchor);
        await dispatcher.DisposeAsync();
        var context = new AgentRequestContext(1, "1", "session", 1, 10_000);

        var exception = Assert.Throws<AgentOperationDispatchException>(() =>
            dispatcher.InvokeAsync(() => 42, context, CancellationToken.None)
                .GetAwaiter()
                .GetResult());

        Assert.Equal(OperationErrorCode.ExecutionContextUnavailable, exception.Error.Code);
    }
}
