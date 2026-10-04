using NativeSpy.Agent;

namespace NativeSpy.Agent.Host;

public interface IAgentHostCompositionFactory
{
    IAgentHostComposition Create(
        IManagedObjectReferenceService identityService,
        HostSessionContext context);
}

public interface IAgentHostComposition : IAsyncDisposable
{
    IReadOnlyList<IAgentOperationHandler> Handlers { get; }
}
