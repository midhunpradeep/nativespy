using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Agent;

public sealed class ManagedObjectRegistrationResult
{
    private ManagedObjectRegistrationResult(
        ManagedObjectRefDto? reference,
        OperationErrorDto? error)
    {
        Reference = reference;
        Error = error;
    }

    public ManagedObjectRefDto? Reference { get; }

    public OperationErrorDto? Error { get; }

    public bool IsSuccess => Reference is not null;

    public static ManagedObjectRegistrationResult Success(ManagedObjectRefDto reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Handle.Kind != HandleKind.ClrObject)
        {
            throw new ArgumentException(
                "A managed object registration requires a ClrObject handle.",
                nameof(reference));
        }

        if (reference.TypeIdentity is null)
        {
            throw new ArgumentException(
                "A managed object registration requires a complete type identity.",
                nameof(reference));
        }

        return new ManagedObjectRegistrationResult(reference, error: null);
    }

    public static ManagedObjectRegistrationResult Failure(OperationErrorDto error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ManagedObjectRegistrationResult(reference: null, error);
    }
}
