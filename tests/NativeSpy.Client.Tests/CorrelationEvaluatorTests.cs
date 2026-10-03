using NativeSpy.Client.Correlation;
using NativeSpy.Protocol.Correlation;
using Xunit;

namespace NativeSpy.Client.Tests;

public sealed class CorrelationEvaluatorTests
{
    [Fact]
    public void Zero_candidates_are_unresolved()
    {
        var decision = Evaluate(_ => { });

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
        Assert.Equal(CorrelationDecisionReason.NoCandidates, decision.DecisionReason);
    }

    [Fact]
    public void One_fully_proven_current_candidate_is_exact()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("IdentityProof", ProofOutcome.Passed)
            .RequireExactStep("IdentityProof")
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Exact, decision.Status);
        Assert.Equal("candidate-1", decision.PrimaryCandidateId);
        Assert.Equal(CorrelationDecisionReason.ExactProofSatisfied, decision.DecisionReason);
    }

    [Fact]
    public void Multiple_candidates_are_ambiguous_without_a_heuristic_winner()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate("candidate-1")
            .WithCandidate("candidate-2")
            .WithProofStep("IdentityProof", ProofOutcome.Passed)
            .RequireExactStep("IdentityProof")
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Ambiguous, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
        Assert.Equal(CorrelationDecisionReason.MultipleCandidates, decision.DecisionReason);
    }

    [Fact]
    public void Required_proof_conflict_is_ambiguous()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithConflict("IdentityProof")
            .RequireExactStep("IdentityProof")
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Ambiguous, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
        Assert.Equal(CorrelationDecisionReason.RequiredEvidenceConflict, decision.DecisionReason);
    }

    [Fact]
    public void Required_validation_conflict_is_ambiguous()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithConflict("CurrentGeneration", validation: true)
            .RequireValidation("CurrentGeneration")
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Ambiguous, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
        Assert.Equal(CorrelationDecisionReason.RequiredEvidenceConflict, decision.DecisionReason);
    }

    [Fact]
    public void High_confidence_requires_permitted_unavailable_deterministic_proof()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("ProviderIdentity", ProofOutcome.NotAvailable)
            .AllowUnavailableForHighConfidence("ProviderIdentity")
            .RequireValidation("CurrentGeneration")
            .WithValidationCheck("CurrentGeneration", ValidationOutcome.Passed)
            .WithStrongEvidence()
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.HighConfidence, decision.Status);
        Assert.Equal("candidate-1", decision.PrimaryCandidateId);
        Assert.Equal(CorrelationDecisionReason.StrongEvidenceDeterministicStepUnavailable, decision.DecisionReason);
    }

    [Fact]
    public void Strong_evidence_false_cannot_produce_high_confidence()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("ProviderIdentity", ProofOutcome.NotAvailable)
            .AllowUnavailableForHighConfidence("ProviderIdentity")
            .WithStrongEvidence(false)
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Theory]
    [InlineData(ProofOutcome.Failed)]
    [InlineData(ProofOutcome.NotAttempted)]
    public void Failed_or_not_attempted_deterministic_proof_cannot_produce_high_confidence(ProofOutcome outcome)
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("ProviderIdentity", outcome)
            .AllowUnavailableForHighConfidence("ProviderIdentity")
            .WithStrongEvidence()
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Conflicted_allowed_deterministic_proof_is_ambiguous_not_high_confidence()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithConflict("ProviderIdentity")
            .AllowUnavailableForHighConfidence("ProviderIdentity")
            .WithStrongEvidence()
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Ambiguous, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Deterministic_not_available_without_permission_is_unresolved()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("ProviderIdentity", ProofOutcome.NotAvailable)
            .RequireExactStep("ProviderIdentity")
            .WithStrongEvidence()
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Theory]
    [InlineData(ProofOutcome.NotAvailable)]
    [InlineData(ProofOutcome.Failed)]
    public void Structural_proof_never_qualifies_as_high_confidence_absence(ProofOutcome outcome)
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("Structure", outcome, EvidenceKind.Structural)
            .RequireExactStep("Structure", EvidenceKind.Structural)
            .WithStrongEvidence()
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Exact_requires_at_least_one_exact_requirement()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("IdentityProof", ProofOutcome.Passed)
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Theory]
    [InlineData(EvidenceKind.Structural)]
    [InlineData(EvidenceKind.Descriptive)]
    [InlineData(EvidenceKind.Geometry)]
    public void Exact_requires_at_least_one_deterministic_requirement(EvidenceKind evidenceKind)
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("WeakProof", ProofOutcome.Passed, evidenceKind)
            .RequireExactStep("WeakProof", evidenceKind)
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Exact_allows_deterministic_and_structural_requirements_when_both_pass()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("DeterministicProof", ProofOutcome.Passed)
            .WithProofStep("StructuralProof", ProofOutcome.Passed, EvidenceKind.Structural)
            .RequireExactStep("DeterministicProof")
            .RequireExactStep("StructuralProof", EvidenceKind.Structural)
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Exact, decision.Status);
        Assert.Equal("candidate-1", decision.PrimaryCandidateId);
    }

    [Fact]
    public void Deterministic_passed_plus_structural_not_available_is_unresolved()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("DeterministicProof", ProofOutcome.Passed)
            .WithProofStep("StructuralProof", ProofOutcome.NotAvailable, EvidenceKind.Structural)
            .RequireExactStep("DeterministicProof")
            .RequireExactStep("StructuralProof", EvidenceKind.Structural)
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Missing_required_proof_is_unresolved()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .RequireExactStep("MissingProof")
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Missing_required_validation_is_unresolved()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("IdentityProof", ProofOutcome.Passed)
            .RequireExactStep("IdentityProof")
            .RequireValidation("CurrentGeneration")
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
        Assert.Equal(CorrelationDecisionReason.LifecycleNotCurrent, decision.DecisionReason);
    }

    [Theory]
    [InlineData(ValidationOutcome.Changed)]
    [InlineData(ValidationOutcome.NotAvailable)]
    [InlineData(ValidationOutcome.Failed)]
    public void Non_passing_required_currentness_is_unresolved(ValidationOutcome outcome)
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("IdentityProof", ProofOutcome.Passed)
            .RequireExactStep("IdentityProof")
            .RequireValidation("CurrentGeneration")
            .WithValidationCheck("CurrentGeneration", outcome)
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
        Assert.Equal(CorrelationDecisionReason.LifecycleNotCurrent, decision.DecisionReason);
    }

    [Fact]
    public void Revalidation_must_be_current_even_when_named_checks_pass()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("IdentityProof", ProofOutcome.Passed)
            .RequireExactStep("IdentityProof")
            .RequireValidation("CurrentGeneration")
            .WithValidationCheck("CurrentGeneration", ValidationOutcome.Passed));

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
        Assert.Equal(CorrelationDecisionReason.LifecycleNotCurrent, decision.DecisionReason);
    }

    [Fact]
    public void Positive_semantic_absence_proof_produces_no_direct_mapping()
    {
        var decision = Evaluate(builder => builder
            .WithPositiveNoDirectMappingProof()
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.NoDirectMapping, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
        Assert.Equal(CorrelationDecisionReason.PositiveNoDirectMapping, decision.DecisionReason);
    }

    [Fact]
    public void Zero_candidates_without_positive_absence_proof_are_unresolved()
    {
        var decision = Evaluate(builder => builder
            .WithPositiveNoDirectMappingProof(established: false));

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Provider_unavailable_or_missing_candidates_do_not_imply_no_direct_mapping()
    {
        var decision = Evaluate(_ => { });

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Equal(CorrelationDecisionReason.NoCandidates, decision.DecisionReason);
    }

    [Fact]
    public void ProviderAware_hint_does_not_escalate_a_weak_conservative_result()
    {
        var withoutHint = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("ProviderIdentity", ProofOutcome.NotAvailable)
            .RequireExactStep("ProviderIdentity")
            .WithStrongEvidence()
            .WithCurrentValidation());
        var withHint = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("ProviderIdentity", ProofOutcome.NotAvailable)
            .RequireExactStep("ProviderIdentity")
            .WithStrongEvidence()
            .WithCurrentValidation()
            .WithProviderAwareHint());

        Assert.Equal(CorrelationStatus.Unresolved, withoutHint.Status);
        Assert.Equal(withoutHint.Status, withHint.Status);
        Assert.Null(withHint.PrimaryCandidateId);
    }

    [Theory]
    [InlineData(EffectCategory.ProviderStateCreating)]
    [InlineData(EffectCategory.AccessibilityStateCreating)]
    public void Effects_do_not_change_an_exact_decision(EffectCategory effectCategory)
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("IdentityProof", ProofOutcome.Passed)
            .RequireExactStep("IdentityProof")
            .WithCurrentValidation()
            .WithEffects(effectCategory));

        Assert.Equal(CorrelationStatus.Exact, decision.Status);
        Assert.Equal("candidate-1", decision.PrimaryCandidateId);
    }

    [Fact]
    public void Descriptive_evidence_alone_is_not_exact()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("NameMatch", ProofOutcome.Passed, EvidenceKind.Descriptive)
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Geometry_evidence_alone_is_not_exact()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("BoundsMatch", ProofOutcome.Passed, EvidenceKind.Geometry)
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Relationship_kind_does_not_itself_produce_exact()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithRelationship(RelationshipKind.SameManagedElement)
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Relationship_kind_remains_orthogonal_when_exact_proof_is_present()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithRelationship(RelationshipKind.GridCell)
            .WithProofStep("GridProviderClosure", ProofOutcome.Passed)
            .RequireExactStep("GridProviderClosure")
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Exact, decision.Status);
        Assert.Equal("candidate-1", decision.PrimaryCandidateId);
    }

    [Fact]
    public void Public_proof_evidence_does_not_define_exact_requirements()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("RuntimeIdCompare", ProofOutcome.Passed)
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Deterministic_identity_proof_with_complete_requirements_can_be_exact()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("DeterministicIdentityProof", ProofOutcome.Passed)
            .RequireExactStep("DeterministicIdentityProof")
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Exact, decision.Status);
        Assert.Equal("candidate-1", decision.PrimaryCandidateId);
    }

    [Fact]
    public void Adapter_metadata_and_effects_do_not_produce_a_status_without_requirements()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithEffects(EffectCategory.ProviderStateCreating)
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Non_required_conflict_does_not_invalidate_required_exact_proof()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithConflict("UnrelatedObservation")
            .WithProofStep("IdentityProof", ProofOutcome.Passed)
            .RequireExactStep("IdentityProof")
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Exact, decision.Status);
        Assert.Equal("candidate-1", decision.PrimaryCandidateId);
    }

    [Fact]
    public void Non_required_failure_does_not_invalidate_required_exact_proof()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("UnrelatedObservation", ProofOutcome.Failed)
            .WithProofStep("IdentityProof", ProofOutcome.Passed)
            .RequireExactStep("IdentityProof")
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Exact, decision.Status);
        Assert.Equal("candidate-1", decision.PrimaryCandidateId);
    }

    [Fact]
    public void Proof_requirement_requires_exact_evidence_kind_match()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("IdentityProof", ProofOutcome.Passed, EvidenceKind.Structural)
            .RequireExactStep("IdentityProof", EvidenceKind.Deterministic)
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Multiple_eligible_unavailable_deterministic_steps_still_produce_high_confidence()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("ProviderIdentity", ProofOutcome.NotAvailable)
            .WithProofStep("OwnerIdentity", ProofOutcome.NotAvailable)
            .AllowUnavailableForHighConfidence("ProviderIdentity")
            .AllowUnavailableForHighConfidence("OwnerIdentity")
            .RequireValidation("CurrentGeneration")
            .WithValidationCheck("CurrentGeneration", ValidationOutcome.Passed)
            .WithStrongEvidence()
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.HighConfidence, decision.Status);
        Assert.Equal("candidate-1", decision.PrimaryCandidateId);
    }

    [Fact]
    public void Eligible_unavailable_deterministic_step_can_combine_with_passed_required_proof()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("ProviderIdentity", ProofOutcome.NotAvailable)
            .WithProofStep("OwnerIdentity", ProofOutcome.Passed)
            .AllowUnavailableForHighConfidence("ProviderIdentity")
            .RequireExactStep("OwnerIdentity")
            .WithStrongEvidence()
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.HighConfidence, decision.Status);
        Assert.Equal("candidate-1", decision.PrimaryCandidateId);
    }

    [Theory]
    [InlineData(ProofOutcome.Failed)]
    [InlineData(ProofOutcome.NotAttempted)]
    public void Eligible_unavailable_step_with_another_non_passing_required_step_is_unresolved(ProofOutcome otherOutcome)
    {
        var decision = Evaluate(builder => builder
            .WithCandidate()
            .WithProofStep("ProviderIdentity", ProofOutcome.NotAvailable)
            .WithProofStep("OwnerIdentity", otherOutcome)
            .AllowUnavailableForHighConfidence("ProviderIdentity")
            .RequireExactStep("OwnerIdentity")
            .WithStrongEvidence()
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Unresolved, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Multiple_candidates_never_receive_a_primary_even_when_one_is_fully_proven()
    {
        var decision = Evaluate(builder => builder
            .WithCandidate("candidate-1")
            .WithCandidate("candidate-2")
            .WithProofStep("IdentityProof", ProofOutcome.Passed)
            .RequireExactStep("IdentityProof")
            .WithCurrentValidation());

        Assert.Equal(CorrelationStatus.Ambiguous, decision.Status);
        Assert.Null(decision.PrimaryCandidateId);
    }

    [Fact]
    public void Duplicate_normalized_fact_names_are_rejected()
    {
        var builder = new CorrelationScenarioBuilder()
            .WithCandidate()
            .WithProofStep("Duplicate", ProofOutcome.Passed)
            .WithProofStep("Duplicate", ProofOutcome.Passed);

        Assert.Throws<ArgumentException>(() => builder.BuildInput());
    }

    [Fact]
    public void Duplicate_normalized_requirement_names_are_rejected()
    {
        var builder = new CorrelationScenarioBuilder()
            .WithCandidate()
            .RequireExactStep("Duplicate")
            .RequireExactStep("Duplicate");

        Assert.Throws<ArgumentException>(() => builder.BuildInput());
    }

    [Fact]
    public void Duplicate_candidate_ids_are_rejected()
    {
        var builder = new CorrelationScenarioBuilder()
            .WithCandidate("same")
            .WithCandidate("same");

        Assert.Throws<ArgumentException>(() => builder.BuildInput());
    }

    [Fact]
    public void Only_deterministic_requirements_may_allow_high_confidence_absence()
    {
        Assert.Throws<ArgumentException>(() =>
            new ProofRequirement("Structure", EvidenceKind.Structural, unavailableForHighConfidenceAllowed: true));
    }

    [Fact]
    public void Policy_hint_consistency_is_validated_before_evaluation()
    {
        var invalidProviderHint = new CorrelationScenarioBuilder()
            .WithCandidate()
            .WithProviderAwareHint()
            .WithPolicy(CorrelationPolicyMode.ProviderAware);

        Assert.Throws<ArgumentException>(() => invalidProviderHint.BuildInput());

        Assert.Throws<ArgumentException>(() => new CorrelationEvaluationInput(
            Array.Empty<CorrelationCandidateAssessment>(),
            new CorrelationPolicyDto(CorrelationPolicyMode.Conservative, 1, 1, 1),
            requiredPolicy: CorrelationPolicyMode.ProviderAware));
    }

    private static CorrelationEvaluationDecision Evaluate(Action<CorrelationScenarioBuilder> configure)
    {
        var builder = new CorrelationScenarioBuilder();
        configure(builder);
        return new CorrelationEvaluator().Evaluate(builder.BuildInput());
    }
}
