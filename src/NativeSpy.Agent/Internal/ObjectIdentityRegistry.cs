using System.Runtime.CompilerServices;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Agent.Internal;

internal sealed class ObjectIdentityRegistry
{
    private ConditionalWeakTable<object, ObjectIdentityEntry> _entries = new();
    private Dictionary<string, HandleRecord> _records = new(StringComparer.Ordinal);

    public int Count => _records.Count;

    public bool TryGet(object target, out ObjectIdentityEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _entries.TryGetValue(target, out entry);
    }

    public void Add(object target, ManagedObjectRefDto reference)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(reference);
        _entries.Add(target, new ObjectIdentityEntry(reference));
        _records.Add(reference.Handle.HandleId, new HandleRecord(reference.Handle, target));
    }

    public bool TryGetRecord(string handleId, out HandleRecord? record)
    {
        return _records.TryGetValue(handleId, out record);
    }

    public void Clear()
    {
        _records.Clear();
        _entries = new ConditionalWeakTable<object, ObjectIdentityEntry>();
    }
}
