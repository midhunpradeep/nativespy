using System.Runtime.CompilerServices;
using NativeSpy.Protocol.Common;

namespace NativeSpy.Agent.Internal;

internal sealed class TypeIdentityRegistry
{
    private readonly ConditionalWeakTable<Type, TypeIdentityEntry> _entries = new();

    public bool TryGet(Type type, out TypeIdentityEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _entries.TryGetValue(type, out entry);
    }

    public void Add(Type type, TypeIdentityDto identity)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(identity);
        _entries.Add(type, new TypeIdentityEntry(identity));
    }

    public void Clear()
    {
        _entries.Clear();
    }
}
