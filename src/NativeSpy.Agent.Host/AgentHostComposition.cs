using NativeSpy.Agent;

namespace NativeSpy.Agent.Host;

public interface IAgentHostCompositionFactory
{
    IReadOnlyList<string> DeclaredOperationNames { get; }

    IAgentHostComposition Create(
        IManagedObjectReferenceService identityService,
        HostSessionContext context);
}

public interface IAgentHostComposition : IAsyncDisposable
{
    IReadOnlyList<IAgentOperationHandler> Handlers { get; }
}
