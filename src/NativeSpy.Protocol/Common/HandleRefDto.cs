namespace NativeSpy.Protocol.Common;

public sealed class HandleRefDto
{
    public HandleRefDto(
        string sessionId,
        string handleId,
        long generation,
        HandleKind kind,
        string? boundaryId = null)
    {
        SessionId = ContractValidation.RequiredIdentifier(sessionId, nameof(sessionId));
        HandleId = ContractValidation.RequiredIdentifier(handleId, nameof(handleId));
        if (generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), generation, "Handle generation must be positive.");
        }

        Generation = generation;
        Kind = ContractValidation.RequireDefinedEnum(kind, nameof(kind));
        BoundaryId = ContractValidation.OptionalIdentifier(boundaryId, nameof(boundaryId));
    }

    public string SessionId { get; }

    public string HandleId { get; }

    public long Generation { get; }

    public HandleKind Kind { get; }

    public string? BoundaryId { get; }
}
