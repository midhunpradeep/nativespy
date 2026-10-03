using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class RelationshipClaimDto
{
    public RelationshipClaimDto(
        RelationshipKind kind,
        CorrelationTargetRefDto targetRef,
        RelationshipScope scope,
        CorrelationTargetRefDto? sourceTargetRef = null,
        IEnumerable<GenerationRefDto>? generationRefs = null,
        AdapterMetadataDto? adapterMetadata = null)
    {
        Kind = ContractValidation.RequireDefinedEnum(kind, nameof(kind));
        TargetRef = targetRef ?? throw new ArgumentNullException(nameof(targetRef));
        Scope = ContractValidation.RequireDefinedEnum(scope, nameof(scope));
        SourceTargetRef = sourceTargetRef;
        GenerationRefs = generationRefs is null
            ? Array.Empty<GenerationRefDto>()
            : ContractValidation.CopyRequired(generationRefs, nameof(generationRefs));
        AdapterMetadata = adapterMetadata;
    }

    public RelationshipKind Kind { get; }

    public CorrelationTargetRefDto? SourceTargetRef { get; }

    public CorrelationTargetRefDto TargetRef { get; }

    public RelationshipScope Scope { get; }

    public IReadOnlyList<GenerationRefDto> GenerationRefs { get; }

    public AdapterMetadataDto? AdapterMetadata { get; }
}
