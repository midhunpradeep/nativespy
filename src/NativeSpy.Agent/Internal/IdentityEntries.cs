using System.Runtime.Loader;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Agent.Internal;

internal enum HandleRecordState
{
    Live,
    Collected
}

internal sealed class ObjectIdentityEntry
{
    public ObjectIdentityEntry(ManagedObjectRefDto reference)
    {
        Reference = reference ?? throw new ArgumentNullException(nameof(reference));
    }

    public ManagedObjectRefDto Reference { get; }
}

internal sealed class HandleRecord
{
    public HandleRecord(HandleRefDto handle, object target)
    {
        Handle = handle ?? throw new ArgumentNullException(nameof(handle));
        Target = new WeakReference<object>(target ?? throw new ArgumentNullException(nameof(target)));
        State = HandleRecordState.Live;
    }

    public HandleRefDto Handle { get; }

    public WeakReference<object> Target { get; }

    public HandleRecordState State { get; private set; }

    public void MarkCollected()
    {
        State = HandleRecordState.Collected;
    }
}

internal sealed class TypeIdentityEntry
{
    public TypeIdentityEntry(TypeIdentityDto identity)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public TypeIdentityDto Identity { get; }
}

internal enum RuntimeBoundaryKind
{
    AssemblyLoadContext
}

internal sealed class RuntimeBoundaryEntry
{
    public RuntimeBoundaryEntry(
        string boundaryId,
        RuntimeBoundaryKind kind,
        string? description)
    {
        BoundaryId = string.IsNullOrWhiteSpace(boundaryId)
            ? throw new ArgumentException("Boundary ID must not be empty.", nameof(boundaryId))
            : boundaryId;
        Kind = kind;
        Description = description;
    }

    public string BoundaryId { get; }

    public RuntimeBoundaryKind Kind { get; }

    public string? Description { get; }
}

internal sealed class TypeObservation
{
    public TypeObservation(
        Type type,
        AssemblyLoadContext loadContext,
        string fullName,
        string assemblySimpleName,
        string? assemblyVersion,
        string? moduleVersionId)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        LoadContext = loadContext ?? throw new ArgumentNullException(nameof(loadContext));
        FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
        AssemblySimpleName = assemblySimpleName ?? throw new ArgumentNullException(nameof(assemblySimpleName));
        AssemblyVersion = assemblyVersion;
        ModuleVersionId = moduleVersionId;
    }

    public Type Type { get; }

    public AssemblyLoadContext LoadContext { get; }

    public string FullName { get; }

    public string AssemblySimpleName { get; }

    public string? AssemblyVersion { get; }

    public string? ModuleVersionId { get; }
}
