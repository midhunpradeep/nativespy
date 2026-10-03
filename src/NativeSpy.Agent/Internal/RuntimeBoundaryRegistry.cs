using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace NativeSpy.Agent.Internal;

internal sealed class RuntimeBoundaryRegistry
{
    private readonly ConditionalWeakTable<AssemblyLoadContext, RuntimeBoundaryEntry> _entries = new();

    public RuntimeBoundaryEntry GetOrCreate(
        AssemblyLoadContext loadContext,
        Func<string> boundaryIdFactory,
        string? description)
    {
        ArgumentNullException.ThrowIfNull(loadContext);
        ArgumentNullException.ThrowIfNull(boundaryIdFactory);

        if (_entries.TryGetValue(loadContext, out var existing))
        {
            return existing;
        }

        var created = new RuntimeBoundaryEntry(
            boundaryIdFactory(),
            RuntimeBoundaryKind.AssemblyLoadContext,
            description);
        _entries.Add(loadContext, created);
        return created;
    }

    public void Clear()
    {
        _entries.Clear();
    }
}
