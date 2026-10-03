using NativeSpy.Protocol.Common;

namespace NativeSpy.Protocol.Correlation;

/// <summary>
/// Detached target/framework facts returned before Client normalization.
/// </summary>
public sealed class FrameworkCorrelationEvidenceDto
{
    public FrameworkCorrelationEvidenceDto(
        string adapterId,
        int processId,
        CorrelationTargetRefDto? candidateTarget,
        IEnumerable<CorrelationEvidenceFactDto> evidenceFacts,
        IEnumerable<CorrelationValidationFactDto> validationFacts,
        CorrelationEffectSummaryDto effects,
        IEnumerable<AdapterMetadataDto> adapterMetadata,
        IEnumerable<CorrelationLimitationDto> limitations,
        OperationErrorDto? operationError = null)
    {
        AdapterId = ContractValidation.RequiredIdentifier(adapterId, nameof(adapterId));
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "ProcessId must be positive.");
        }

        ProcessId = processId;
        CandidateTarget = candidateTarget;
        EvidenceFacts = ContractValidation.CopyRequired(evidenceFacts, nameof(evidenceFacts));
        ValidationFacts = ContractValidation.CopyRequired(validationFacts, nameof(validationFacts));
        Effects = effects ?? throw new ArgumentNullException(nameof(effects));
        AdapterMetadata = ContractValidation.CopyRequired(adapterMetadata, nameof(adapterMetadata));
        Limitations = ContractValidation.CopyRequired(limitations, nameof(limitations));
        OperationError = operationError;
    }

    public string AdapterId { get; }

    public int ProcessId { get; }

    public CorrelationTargetRefDto? CandidateTarget { get; }

    public IReadOnlyList<CorrelationEvidenceFactDto> EvidenceFacts { get; }

    public IReadOnlyList<CorrelationValidationFactDto> ValidationFacts { get; }

    public CorrelationEffectSummaryDto Effects { get; }

    public IReadOnlyList<AdapterMetadataDto> AdapterMetadata { get; }

    public IReadOnlyList<CorrelationLimitationDto> Limitations { get; }

    public OperationErrorDto? OperationError { get; }
}
