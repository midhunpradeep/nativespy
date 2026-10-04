using System.Windows.Forms;
using NativeSpy.Agent.Host;

namespace NativeSpy.Agent.Host.WinForms;

internal sealed class WinFormsClrExecutionContextAdapter : IClrExecutionContextAdapter
{
    private readonly IWinFormsTargetDispatcher _dispatcher;

    public WinFormsClrExecutionContextAdapter(IWinFormsTargetDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool Requires(object target)
    {
        // A Control remains UI-affine even after disposal. Claiming it here
        // keeps reflection from silently falling back to the Host thread;
        // the dispatcher then reports the explicit unavailable outcome.
        return target is Control;
    }

    public Task<object?> InvokeAsync(
        Func<object?> callback,
        AgentRequestContext context,
        CancellationToken cancellationToken)
    {
        return _dispatcher
            .InvokeAsync(callback, context, cancellationToken)
            .ContinueWith(
                task => (object?)task.GetAwaiter().GetResult(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }
}
