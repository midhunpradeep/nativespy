using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Correlation;

internal sealed class CorrelationEvaluationDecision
{
    public CorrelationEvaluationDecision(
        CorrelationStatus status,
        string? primaryCandidateId,
        CorrelationDecisionReason decisionReason)
    {
        Status = status;
        PrimaryCandidateId = primaryCandidateId;
        DecisionReason = decisionReason;

        var requiresPrimary = status is CorrelationStatus.Exact or CorrelationStatus.HighConfidence;
        if (requiresPrimary && string.IsNullOrWhiteSpace(primaryCandidateId))
        {
            throw new ArgumentException("This status requires a primary candidate.", nameof(primaryCandidateId));
        }

        if (!requiresPrimary && primaryCandidateId is not null)
        {
            throw new ArgumentException("This status cannot select a primary candidate.", nameof(primaryCandidateId));
        }
    }

    public CorrelationStatus Status { get; }

    public string? PrimaryCandidateId { get; }

    public CorrelationDecisionReason DecisionReason { get; }
}
