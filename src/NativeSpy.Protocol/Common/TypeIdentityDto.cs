namespace NativeSpy.Protocol.Common;

public sealed class TypeIdentityDto
{
    public TypeIdentityDto(
        string typeId,
        string fullName,
        string assemblySimpleName,
        string boundaryId,
        bool isValueType,
        IEnumerable<TypeRefDto> genericArguments,
        IEnumerable<TypeRefDto> interfaces,
        string? assemblyVersion = null,
        string? assemblyCulture = null,
        string? publicKeyToken = null,
        string? moduleVersionId = null,
        TypeRefDto? declaringType = null,
        TypeRefDto? genericDefinition = null,
        int? arrayRank = null,
        IEnumerable<int>? arrayShape = null,
        TypeRefDto? pointerElementType = null,
        TypeRefDto? byRefElementType = null,
        TypeRefDto? nullableUnderlyingType = null,
        TypeRefDto? baseType = null,
        string? dynamicIdentity = null)
    {
        TypeId = ContractValidation.RequiredIdentifier(typeId, nameof(typeId));
        FullName = ContractValidation.RequiredText(fullName, nameof(fullName));
        AssemblySimpleName = ContractValidation.RequiredText(assemblySimpleName, nameof(assemblySimpleName));
        BoundaryId = ContractValidation.RequiredIdentifier(boundaryId, nameof(boundaryId));
        IsValueType = isValueType;
        GenericArguments = ContractValidation.CopyRequired(genericArguments, nameof(genericArguments));
        Interfaces = ContractValidation.CopyRequired(interfaces, nameof(interfaces));

        AssemblyVersion = ContractValidation.OptionalText(assemblyVersion, nameof(assemblyVersion));
        AssemblyCulture = ContractValidation.OptionalText(assemblyCulture, nameof(assemblyCulture));
        PublicKeyToken = ContractValidation.OptionalText(publicKeyToken, nameof(publicKeyToken));
        ModuleVersionId = ContractValidation.OptionalIdentifier(moduleVersionId, nameof(moduleVersionId));
        DeclaringType = declaringType;
        GenericDefinition = genericDefinition;

        if (arrayRank is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayRank), arrayRank, "Array rank must be positive when supplied.");
        }

        ArrayRank = arrayRank;
        ArrayShape = arrayShape is null
            ? null
            : ContractValidation.CopyRequired(arrayShape, nameof(arrayShape));

        if (ArrayShape is not null && ArrayRank is null)
        {
            throw new ArgumentException("An array shape requires an array rank.", nameof(arrayShape));
        }

        if (ArrayShape is not null && ArrayRank != ArrayShape.Count)
        {
            throw new ArgumentException("Array shape length must match array rank.", nameof(arrayShape));
        }

        PointerElementType = pointerElementType;
        ByRefElementType = byRefElementType;
        NullableUnderlyingType = nullableUnderlyingType;
        BaseType = baseType;
        DynamicIdentity = ContractValidation.OptionalIdentifier(dynamicIdentity, nameof(dynamicIdentity));
    }

    public string TypeId { get; }

    public string FullName { get; }

    public string AssemblySimpleName { get; }

    public string? AssemblyVersion { get; }

    public string? AssemblyCulture { get; }

    public string? PublicKeyToken { get; }

    public string? ModuleVersionId { get; }

    public string BoundaryId { get; }

    public TypeRefDto? DeclaringType { get; }

    public TypeRefDto? GenericDefinition { get; }

    public IReadOnlyList<TypeRefDto> GenericArguments { get; }

    public int? ArrayRank { get; }

    public IReadOnlyList<int>? ArrayShape { get; }

    public TypeRefDto? PointerElementType { get; }

    public TypeRefDto? ByRefElementType { get; }

    public TypeRefDto? NullableUnderlyingType { get; }

    public bool IsValueType { get; }

    public TypeRefDto? BaseType { get; }

    public IReadOnlyList<TypeRefDto> Interfaces { get; }

    public string? DynamicIdentity { get; }
}
