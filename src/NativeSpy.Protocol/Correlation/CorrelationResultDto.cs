using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

public sealed class CorrelationResultDto
{
    public CorrelationResultDto(
        CorrelationStatus status,
        CorrelationDirection direction,
        CorrelationPolicyDto policy,
        CorrelationSourceDto source,
        IEnumerable<CorrelationCandidateDto> candidates,
        string? primaryCandidateId,
        CorrelationProofSummaryDto? proofSummary,
        CorrelationEffectSummaryDto effects,
        CorrelationValidationDto validation,
        IEnumerable<CorrelationConflictDto> conflicts,
        IEnumerable<CorrelationLimitationDto> limitations,
        bool deeperProofAvailable,
        CorrelationPolicyMode? requiredPolicy = null,
        OperationErrorDto? operationError = null)
    {
        Status = ContractValidation.RequireDefinedEnum(status, nameof(status));
        Direction = ContractValidation.RequireDefinedEnum(direction, nameof(direction));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Candidates = ContractValidation.CopyRequired(candidates, nameof(candidates));
        EnsureUniqueCandidateIds(Candidates);
        PrimaryCandidateId = ContractValidation.OptionalIdentifier(primaryCandidateId, nameof(primaryCandidateId));
        ProofSummary = proofSummary;
        Effects = effects ?? throw new ArgumentNullException(nameof(effects));
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
        Conflicts = ContractValidation.CopyRequired(conflicts, nameof(conflicts));
        Limitations = ContractValidation.CopyRequired(limitations, nameof(limitations));
        DeeperProofAvailable = deeperProofAvailable;
        RequiredPolicy = requiredPolicy is null
            ? null
            : ContractValidation.RequireDefinedEnum(requiredPolicy.Value, nameof(requiredPolicy));
        OperationError = operationError;

        ValidateSelection();
        ValidatePolicyHint();
    }

    public CorrelationStatus Status { get; }

    public CorrelationDirection Direction { get; }

    public CorrelationPolicyDto Policy { get; }

    public CorrelationSourceDto Source { get; }

    public IReadOnlyList<CorrelationCandidateDto> Candidates { get; }

    public string? PrimaryCandidateId { get; }

    public CorrelationProofSummaryDto? ProofSummary { get; }

    public CorrelationEffectSummaryDto Effects { get; }

    public CorrelationValidationDto Validation { get; }

    public IReadOnlyList<CorrelationConflictDto> Conflicts { get; }

    public IReadOnlyList<CorrelationLimitationDto> Limitations { get; }

    public bool DeeperProofAvailable { get; }

    public CorrelationPolicyMode? RequiredPolicy { get; }

    public OperationErrorDto? OperationError { get; }

    private void ValidateSelection()
    {
        var requiresPrimary = Status is CorrelationStatus.Exact or CorrelationStatus.HighConfidence;
        if (requiresPrimary)
        {
            if (Candidates.Count != 1 || PrimaryCandidateId is null)
            {
                throw new ArgumentException("Exact and HighConfidence results require one selected candidate.");
            }

            if (!string.Equals(PrimaryCandidateId, Candidates[0].CandidateId, StringComparison.Ordinal))
            {
                throw new ArgumentException("PrimaryCandidateId must refer to the sole candidate.", nameof(PrimaryCandidateId));
            }
        }
        else if (PrimaryCandidateId is not null)
        {
            throw new ArgumentException("Only Exact and HighConfidence results may select a primary candidate.", nameof(PrimaryCandidateId));
        }

        if (OperationError is not null && Status != CorrelationStatus.Unresolved)
        {
            throw new ArgumentException("Operation errors may only accompany Unresolved results.", nameof(OperationError));
        }
    }

    private void ValidatePolicyHint()
    {
        if (RequiredPolicy == CorrelationPolicyMode.Conservative)
        {
            throw new ArgumentException("RequiredPolicy may only advertise ProviderAware.", nameof(RequiredPolicy));
        }

        if (RequiredPolicy is not null && !DeeperProofAvailable)
        {
            throw new ArgumentException("A required policy requires deeper proof to be available.", nameof(RequiredPolicy));
        }

        if (RequiredPolicy == CorrelationPolicyMode.ProviderAware && Policy.Mode != CorrelationPolicyMode.Conservative)
        {
            throw new ArgumentException(
                "ProviderAware may only be advertised as a deeper policy from Conservative mode.",
                nameof(RequiredPolicy));
        }
    }

    private static void EnsureUniqueCandidateIds(IEnumerable<CorrelationCandidateDto> candidates)
    {
        var ids = candidates.Select(static candidate => candidate.CandidateId).ToArray();
        ContractValidation.RequireUniqueIdentifiers(ids, nameof(candidates));
    }
}
