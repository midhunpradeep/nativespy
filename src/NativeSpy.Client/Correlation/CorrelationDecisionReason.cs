namespace NativeSpy.Client.Correlation;

internal enum CorrelationDecisionReason
{
    PositiveNoDirectMapping,
    MultipleCandidates,
    RequiredEvidenceConflict,
    NoCandidates,
    LifecycleNotCurrent,
    ExactProofSatisfied,
    StrongEvidenceDeterministicStepUnavailable,
    InsufficientEvidence
}
