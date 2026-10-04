using NativeSpy.FlaUI;
using Xunit;

namespace NativeSpy.FlaUI.Tests;

public sealed class FlaUiMtaExecutorTests
{
    [Fact]
    public async Task Timeout_quarantines_the_worker_and_drops_queued_ui_work()
    {
        var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var executor = new FlaUiMtaExecutor(
            () => exited.TrySetResult(null));

        var running = executor.InvokeAsync(
            () =>
            {
                started.TrySetResult(null);
                release.Task.GetAwaiter().GetResult();
                return 1;
            },
            CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var queued = executor.InvokeAsync(() => 2, CancellationToken.None);

        await Assert.ThrowsAsync<FlaUiSessionException>(() =>
            executor.InvokeWithTimeoutAsync(() => 3, TimeSpan.FromMilliseconds(40), CancellationToken.None));
        Assert.True(executor.IsPoisoned);
        var invocationException = Record.Exception(() =>
        {
            _ = executor.InvokeAsync(() => 4, CancellationToken.None);
        });
        Assert.IsType<FlaUiSessionException>(invocationException);

        release.TrySetResult(null);
        Assert.Equal(1, await running.WaitAsync(TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAsync<FlaUiSessionException>(() => queued.WaitAsync(TimeSpan.FromSeconds(2)));
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
