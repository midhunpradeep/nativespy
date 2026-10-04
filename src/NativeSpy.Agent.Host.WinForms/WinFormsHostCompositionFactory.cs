using System.Text.Json;
using System.Windows.Forms;
using NativeSpy.Agent;
using NativeSpy.Agent.Host;
using NativeSpy.Agent.WinForms;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Json;

namespace NativeSpy.Agent.Host.WinForms;

public sealed class WinFormsHostCompositionFactory : IAgentHostCompositionFactory
{
    private readonly Control _dispatchAnchor;

    public WinFormsHostCompositionFactory(Control dispatchAnchor)
    {
        _dispatchAnchor = dispatchAnchor ?? throw new ArgumentNullException(nameof(dispatchAnchor));
    }

    public IAgentHostComposition Create(
        IManagedObjectReferenceService identityService,
        HostSessionContext context)
    {
        if (identityService is null)
        {
            throw new ArgumentNullException(nameof(identityService));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var dispatcher = new WinFormsUiDispatcher(_dispatchAnchor);
        var adapter = new WinFormsCurrentHwndAdapter(identityService);
        return new WinFormsHostComposition(
            dispatcher,
            new IAgentOperationHandler[]
            {
                new BeginCurrentHwndHandler(adapter, dispatcher),
                new RevalidateCurrentHwndHandler(adapter, dispatcher)
            });
    }
}

internal sealed class WinFormsHostComposition : IAgentHostComposition, IAgentHostAdapterMetadata
{
    private readonly IWinFormsTargetDispatcher _dispatcher;

    public WinFormsHostComposition(
        IWinFormsTargetDispatcher dispatcher,
        IReadOnlyList<IAgentOperationHandler> handlers)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    public IReadOnlyList<IAgentOperationHandler> Handlers { get; }

    public IReadOnlyList<string> AdapterIds { get; } = new[] { "winforms" };

    public async ValueTask DisposeAsync()
    {
        await _dispatcher.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class BeginCurrentHwndHandler : IAgentOperationHandler
{
    private readonly WinFormsCurrentHwndAdapter _adapter;
    private readonly IWinFormsTargetDispatcher _dispatcher;

    public BeginCurrentHwndHandler(
        WinFormsCurrentHwndAdapter adapter,
        IWinFormsTargetDispatcher dispatcher)
    {
        _adapter = adapter;
        _dispatcher = dispatcher;
    }

    public string OperationName => WinFormsOperationNames.BeginCurrentHwnd;

    public async Task<AgentHandlerResult> HandleAsync(
        AgentRequestContext context,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var hwnd = ProtocolJsonCodec.ReadBeginCurrentHwndPayload(payload);
        var evidence = await _dispatcher.InvokeAsync(
                () => _adapter.BeginCurrentHwnd(hwnd),
                context,
                cancellationToken)
            .ConfigureAwait(false);
        return AgentHandlerResult.Success(ProtocolJsonCodec.SerializeFrameworkEvidence(evidence));
    }
}

internal sealed class RevalidateCurrentHwndHandler : IAgentOperationHandler
{
    private readonly WinFormsCurrentHwndAdapter _adapter;
    private readonly IWinFormsTargetDispatcher _dispatcher;

    public RevalidateCurrentHwndHandler(
        WinFormsCurrentHwndAdapter adapter,
        IWinFormsTargetDispatcher dispatcher)
    {
        _adapter = adapter;
        _dispatcher = dispatcher;
    }

    public string OperationName => WinFormsOperationNames.RevalidateCurrentHwnd;

    public async Task<AgentHandlerResult> HandleAsync(
        AgentRequestContext context,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var (hwnd, candidateHandle) = ProtocolJsonCodec.ReadRevalidateCurrentHwndPayload(payload);
        var evidence = await _dispatcher.InvokeAsync(
                () => _adapter.RevalidateCurrentHwnd(hwnd, candidateHandle),
                context,
                cancellationToken)
            .ConfigureAwait(false);
        return AgentHandlerResult.Success(ProtocolJsonCodec.SerializeFrameworkEvidence(evidence));
    }
}

public static class WinFormsOperationNames
{
    public const string BeginCurrentHwnd = "beginCurrentHwnd";
    public const string RevalidateCurrentHwnd = "revalidateCurrentHwnd";
}
