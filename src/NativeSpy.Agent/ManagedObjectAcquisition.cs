using NativeSpy.Protocol.Common;

namespace NativeSpy.Agent;

public sealed class ManagedObjectAcquisition : IDisposable
{
    private object? _target;

    internal ManagedObjectAcquisition(HandleRefDto handle, object target)
    {
        Handle = handle ?? throw new ArgumentNullException(nameof(handle));
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public HandleRefDto Handle { get; }

    public object Target => Volatile.Read(ref _target)
        ?? throw new ObjectDisposedException(nameof(ManagedObjectAcquisition));

    public void Dispose()
    {
        Interlocked.Exchange(ref _target, null);
    }
}
