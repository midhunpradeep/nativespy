using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Correlation;

internal sealed class WinFormsCurrentHwndNormalization
{
    public WinFormsCurrentHwndNormalization(
        IReadOnlyList<CorrelationCandidateDto> candidates,
        IReadOnlyList<CorrelationCandidateAssessment> assessments,
        CorrelationProofSummaryDto? proofSummary,
        CorrelationValidationDto validation,
        CorrelationEffectSummaryDto effects,
        IReadOnlyList<CorrelationLimitationDto> limitations,
        OperationErrorDto? operationError)
    {
        Candidates = candidates;
        Assessments = assessments;
        ProofSummary = proofSummary;
        Validation = validation;
        Effects = effects;
        Limitations = limitations;
        OperationError = operationError;
    }

    public IReadOnlyList<CorrelationCandidateDto> Candidates { get; }

    public IReadOnlyList<CorrelationCandidateAssessment> Assessments { get; }

    public CorrelationProofSummaryDto? ProofSummary { get; }

    public CorrelationValidationDto Validation { get; }

    public CorrelationEffectSummaryDto Effects { get; }

    public IReadOnlyList<CorrelationLimitationDto> Limitations { get; }

    public OperationErrorDto? OperationError { get; }
}
