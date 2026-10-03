namespace NativeSpy.Protocol.Common;

public sealed class TypeRefDto
{
    public TypeRefDto(string typeId, string boundaryId)
    {
        TypeId = ContractValidation.RequiredIdentifier(typeId, nameof(typeId));
        BoundaryId = ContractValidation.RequiredIdentifier(boundaryId, nameof(boundaryId));
    }

    public string TypeId { get; }

    public string BoundaryId { get; }
}
