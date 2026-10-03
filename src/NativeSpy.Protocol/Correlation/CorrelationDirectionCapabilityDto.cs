using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class CorrelationDirectionCapabilityDto
{
    public CorrelationDirectionCapabilityDto(
        CorrelationDirection direction,
        IEnumerable<RelationshipKind> relationshipKinds,
        CorrelationCapabilityMode mode,
        CorrelationPolicyMode minimumPolicy,
        int candidateBound,
        bool requiresExternalSource,
        bool requiresTargetHandle,
        IEnumerable<CorrelationLimitationDto> limitations)
    {
        Direction = direction;
        RelationshipKinds = ContractValidation.CopyRequired(relationshipKinds, nameof(relationshipKinds));
        if (RelationshipKinds.Distinct().Count() != RelationshipKinds.Count)
        {
            throw new ArgumentException("Relationship kinds must be unique.", nameof(relationshipKinds));
        }

        Mode = mode;
        MinimumPolicy = minimumPolicy;
        if (candidateBound <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateBound), candidateBound, "Candidate bound must be positive.");
        }

        CandidateBound = candidateBound;
        RequiresExternalSource = requiresExternalSource;
        RequiresTargetHandle = requiresTargetHandle;
        Limitations = ContractValidation.CopyRequired(limitations, nameof(limitations));
    }

    public CorrelationDirection Direction { get; }

    public IReadOnlyList<RelationshipKind> RelationshipKinds { get; }

    public CorrelationCapabilityMode Mode { get; }

    public CorrelationPolicyMode MinimumPolicy { get; }

    public int CandidateBound { get; }

    public bool RequiresExternalSource { get; }

    public bool RequiresTargetHandle { get; }

    public IReadOnlyList<CorrelationLimitationDto> Limitations { get; }
}
