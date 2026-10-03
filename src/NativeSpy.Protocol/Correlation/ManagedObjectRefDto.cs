using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class ManagedObjectRefDto
{
    public ManagedObjectRefDto(
        HandleRefDto handle,
        TypeIdentityDto? typeIdentity = null,
        string? boundaryId = null,
        string? contextId = null)
    {
        Handle = handle ?? throw new ArgumentNullException(nameof(handle));
        if (handle.Kind != HandleKind.ClrObject)
        {
            throw new ArgumentException("A managed object reference requires a ClrObject handle.", nameof(handle));
        }

        TypeIdentity = typeIdentity;
        BoundaryId = ContractValidation.OptionalIdentifier(boundaryId, nameof(boundaryId));
        ContextId = ContractValidation.OptionalIdentifier(contextId, nameof(contextId));
    }

    public HandleRefDto Handle { get; }

    public TypeIdentityDto? TypeIdentity { get; }

    public string? BoundaryId { get; }

    public string? ContextId { get; }
}
