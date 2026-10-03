using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Correlation;

internal sealed class ProofRequirement
{
    public ProofRequirement(
        string stepName,
        EvidenceKind evidenceKind,
        bool unavailableForHighConfidenceAllowed = false)
    {
        StepName = InternalValidation.RequiredIdentifier(stepName, nameof(stepName));
        EvidenceKind = evidenceKind;
        if (unavailableForHighConfidenceAllowed && evidenceKind != EvidenceKind.Deterministic)
        {
            throw new ArgumentException(
                "Only deterministic proof requirements may allow unavailable evidence for HighConfidence.",
                nameof(unavailableForHighConfidenceAllowed));
        }

        UnavailableForHighConfidenceAllowed = unavailableForHighConfidenceAllowed;
    }

    public string StepName { get; }

    public EvidenceKind EvidenceKind { get; }

    public bool UnavailableForHighConfidenceAllowed { get; }
}
