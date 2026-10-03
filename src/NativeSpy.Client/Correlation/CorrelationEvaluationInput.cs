using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Correlation;

internal sealed class CorrelationEvaluationInput
{
    public CorrelationEvaluationInput(
        IEnumerable<CorrelationCandidateAssessment> candidates,
        CorrelationPolicyDto policy,
        PositiveNoDirectMappingAssessment? positiveNoDirectMappingProof = null,
        bool deeperProofAvailable = false,
        CorrelationPolicyMode? requiredPolicy = null)
    {
        Candidates = InternalValidation.CopyRequired(candidates, nameof(candidates));
        InternalValidation.RequireUniqueIdentifiers(
            Candidates.Select(static candidate => candidate.Candidate.CandidateId),
            nameof(candidates));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        PositiveNoDirectMappingProof = positiveNoDirectMappingProof;
        DeeperProofAvailable = deeperProofAvailable;
        RequiredPolicy = requiredPolicy;
        ValidatePolicyHint();
    }

    public IReadOnlyList<CorrelationCandidateAssessment> Candidates { get; }

    public PositiveNoDirectMappingAssessment? PositiveNoDirectMappingProof { get; }

    public CorrelationPolicyDto Policy { get; }

    public bool DeeperProofAvailable { get; }

    public CorrelationPolicyMode? RequiredPolicy { get; }

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
}
