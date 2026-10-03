using NativeSpy.Protocol.Common;

namespace NativeSpy.Agent;

public interface IManagedObjectReferenceService
{
    ManagedObjectRegistrationResult Register(object target);

    ManagedObjectAcquisitionResult TryAcquire(HandleRefDto handle);
}
