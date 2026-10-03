using NativeSpy.Protocol.Common;

namespace NativeSpy.Agent;

public sealed class ManagedObjectAcquisitionResult
{
    private ManagedObjectAcquisitionResult(
        ManagedObjectAcquisition? acquisition,
        OperationErrorDto? error)
    {
        Acquisition = acquisition;
        Error = error;
    }

    public ManagedObjectAcquisition? Acquisition { get; }

    public OperationErrorDto? Error { get; }

    public bool IsSuccess => Acquisition is not null;

    public static ManagedObjectAcquisitionResult Success(
        HandleRefDto handle,
        object target)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(target);
        if (handle.Kind != HandleKind.ClrObject)
        {
            throw new ArgumentException(
                "A managed object acquisition requires a ClrObject handle.",
                nameof(handle));
        }

        return new ManagedObjectAcquisitionResult(
            new ManagedObjectAcquisition(handle, target),
            error: null);
    }

    public static ManagedObjectAcquisitionResult Failure(OperationErrorDto error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ManagedObjectAcquisitionResult(acquisition: null, error);
    }
}
