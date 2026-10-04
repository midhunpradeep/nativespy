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

    public IReadOnlyList<string> DeclaredOperationNames => ProtocolOperationNames.ProductionOperations;

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

        if (identityService is not ClrAgentSession session)
        {
            throw new InvalidOperationException(
                "The production WinForms composition requires a ClrAgentSession identity service.");
        }

        var dispatcher = new WinFormsUiDispatcher(_dispatchAnchor);
        var executionResolver = new CompositeClrExecutionContextResolver(
            new[] { new WinFormsClrExecutionContextAdapter(dispatcher) });
        var inspection = new ClrInspectionService(session);
        var adapter = new WinFormsCurrentHwndAdapter(identityService);
        var handlers = new List<IAgentOperationHandler>
        {
            new BeginCurrentHwndHandler(adapter, dispatcher),
            new RevalidateCurrentHwndHandler(adapter, dispatcher)
        };
        handlers.AddRange(ClrInspectionHandlerFactory.Create(inspection, executionResolver));
        return new WinFormsHostComposition(dispatcher, inspection, handlers);
    }
}

internal sealed class WinFormsHostComposition : IAgentHostComposition, IAgentHostAdapterMetadata
{
    private readonly IWinFormsTargetDispatcher _dispatcher;
    private readonly ClrInspectionService _inspection;

    public WinFormsHostComposition(
        IWinFormsTargetDispatcher dispatcher,
        ClrInspectionService inspection,
        IReadOnlyList<IAgentOperationHandler> handlers)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _inspection = inspection ?? throw new ArgumentNullException(nameof(inspection));
        Handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    public IReadOnlyList<IAgentOperationHandler> Handlers { get; }

    public IReadOnlyList<string> AdapterIds { get; } = new[] { "winforms" };

    public async ValueTask DisposeAsync()
    {
        _inspection.Dispose();
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

    public string OperationName => ProtocolOperationNames.BeginCurrentHwnd;

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

    public string OperationName => ProtocolOperationNames.RevalidateCurrentHwnd;

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
