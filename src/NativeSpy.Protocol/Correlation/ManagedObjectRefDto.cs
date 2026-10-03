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

        if (typeIdentity is not null)
        {
            if (handle.BoundaryId is null)
            {
                throw new ArgumentException(
                    "A managed object type identity requires a handle runtime boundary.",
                    nameof(handle));
            }

            if (!string.Equals(typeIdentity.BoundaryId, handle.BoundaryId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The managed object type identity boundary must match the handle boundary.",
                    nameof(typeIdentity));
            }
        }

        TypeIdentity = typeIdentity;
        BoundaryId = ContractValidation.OptionalIdentifier(boundaryId, nameof(boundaryId));
        if (BoundaryId is not null
            && !string.Equals(BoundaryId, handle.BoundaryId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The managed object boundary must match the handle boundary.",
                nameof(boundaryId));
        }

        ContextId = ContractValidation.OptionalIdentifier(contextId, nameof(contextId));
    }

    public HandleRefDto Handle { get; }

    public TypeIdentityDto? TypeIdentity { get; }

    public string? BoundaryId { get; }

    public string? ContextId { get; }
}
