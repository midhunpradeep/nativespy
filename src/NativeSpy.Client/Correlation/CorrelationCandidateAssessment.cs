using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Correlation;

internal sealed class CorrelationCandidateAssessment
{
    public CorrelationCandidateAssessment(
        CorrelationCandidateDto candidate,
        IEnumerable<ProofStepDto> proofSteps,
        IEnumerable<ValidationCheckDto> validationChecks,
        IEnumerable<ProofRequirement> exactRequirements,
        IEnumerable<ValidationRequirement> validationRequirements,
        bool strongEvidenceSatisfied)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        ProofSteps = InternalValidation.CopyRequired(proofSteps, nameof(proofSteps));
        InternalValidation.RequireUniqueIdentifiers(
            ProofSteps.Select(static step => step.Name),
            nameof(proofSteps));
        ValidationChecks = InternalValidation.CopyRequired(validationChecks, nameof(validationChecks));
        InternalValidation.RequireUniqueIdentifiers(
            ValidationChecks.Select(static check => check.Name),
            nameof(validationChecks));
        ExactRequirements = InternalValidation.CopyRequired(exactRequirements, nameof(exactRequirements));
        InternalValidation.RequireUniqueIdentifiers(
            ExactRequirements.Select(static requirement => requirement.StepName),
            nameof(exactRequirements));
        ValidationRequirements = InternalValidation.CopyRequired(validationRequirements, nameof(validationRequirements));
        InternalValidation.RequireUniqueIdentifiers(
            ValidationRequirements.Select(static requirement => requirement.CheckName),
            nameof(validationRequirements));
        StrongEvidenceSatisfied = strongEvidenceSatisfied;
    }

    public CorrelationCandidateDto Candidate { get; }

    public IReadOnlyList<ProofStepDto> ProofSteps { get; }

    public IReadOnlyList<ValidationCheckDto> ValidationChecks { get; }

    public IReadOnlyList<ProofRequirement> ExactRequirements { get; }

    public IReadOnlyList<ValidationRequirement> ValidationRequirements { get; }

    public bool StrongEvidenceSatisfied { get; }
}
