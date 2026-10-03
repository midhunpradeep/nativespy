using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class CorrelationCandidateDto
{
    public CorrelationCandidateDto(
        string candidateId,
        CorrelationTargetRefDto target,
        IEnumerable<RelationshipClaimDto> relationships,
        IEnumerable<CorrelationEvidenceSummaryDto> evidenceSummary,
        CorrelationProofSummaryDto? proofSummary,
        CorrelationValidationDto validation,
        CorrelationEffectSummaryDto effects,
        IEnumerable<CorrelationLimitationDto> limitations)
    {
        CandidateId = ContractValidation.RequiredIdentifier(candidateId, nameof(candidateId));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Relationships = ContractValidation.CopyRequired(relationships, nameof(relationships));
        EvidenceSummary = ContractValidation.CopyRequired(evidenceSummary, nameof(evidenceSummary));
        ProofSummary = proofSummary;
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
        Effects = effects ?? throw new ArgumentNullException(nameof(effects));
        Limitations = ContractValidation.CopyRequired(limitations, nameof(limitations));
    }

    public string CandidateId { get; }

    public CorrelationTargetRefDto Target { get; }

    public IReadOnlyList<RelationshipClaimDto> Relationships { get; }

    public IReadOnlyList<CorrelationEvidenceSummaryDto> EvidenceSummary { get; }

    public CorrelationProofSummaryDto? ProofSummary { get; }

    public CorrelationValidationDto Validation { get; }

    public CorrelationEffectSummaryDto Effects { get; }

    public IReadOnlyList<CorrelationLimitationDto> Limitations { get; }
}
