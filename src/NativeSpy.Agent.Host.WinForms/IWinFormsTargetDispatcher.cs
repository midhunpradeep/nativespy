namespace NativeSpy.Agent.Host.WinForms;

internal interface IWinFormsTargetDispatcher : IAsyncDisposable
{
    Task<T> InvokeAsync<T>(
        Func<T> callback,
        NativeSpy.Agent.Host.AgentRequestContext context,
        CancellationToken cancellationToken);
}
