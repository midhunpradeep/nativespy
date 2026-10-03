using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Correlation;

internal sealed class CorrelationEvaluator
{
    public CorrelationEvaluationDecision Evaluate(CorrelationEvaluationInput input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (input.PositiveNoDirectMappingProof?.IsEstablished == true)
        {
            return new CorrelationEvaluationDecision(
                CorrelationStatus.NoDirectMapping,
                primaryCandidateId: null,
                CorrelationDecisionReason.PositiveNoDirectMapping);
        }

        if (input.Candidates.Count > 1)
        {
            return new CorrelationEvaluationDecision(
                CorrelationStatus.Ambiguous,
                primaryCandidateId: null,
                CorrelationDecisionReason.MultipleCandidates);
        }

        if (input.Candidates.Count == 0)
        {
            return new CorrelationEvaluationDecision(
                CorrelationStatus.Unresolved,
                primaryCandidateId: null,
                CorrelationDecisionReason.NoCandidates);
        }

        var assessment = input.Candidates[0];
        if (HasRequiredConflict(assessment))
        {
            return new CorrelationEvaluationDecision(
                CorrelationStatus.Ambiguous,
                primaryCandidateId: null,
                CorrelationDecisionReason.RequiredEvidenceConflict);
        }

        if (!RequiredValidationPasses(assessment) || !assessment.Candidate.Validation.Revalidated)
        {
            return new CorrelationEvaluationDecision(
                CorrelationStatus.Unresolved,
                primaryCandidateId: null,
                CorrelationDecisionReason.LifecycleNotCurrent);
        }

        if (ExactProofPasses(assessment))
        {
            return new CorrelationEvaluationDecision(
                CorrelationStatus.Exact,
                assessment.Candidate.CandidateId,
                CorrelationDecisionReason.ExactProofSatisfied);
        }

        if (HighConfidenceProofPasses(assessment))
        {
            return new CorrelationEvaluationDecision(
                CorrelationStatus.HighConfidence,
                assessment.Candidate.CandidateId,
                CorrelationDecisionReason.StrongEvidenceDeterministicStepUnavailable);
        }

        return new CorrelationEvaluationDecision(
            CorrelationStatus.Unresolved,
            primaryCandidateId: null,
            CorrelationDecisionReason.InsufficientEvidence);
    }

    private static bool HasRequiredConflict(CorrelationCandidateAssessment assessment)
    {
        foreach (var requirement in assessment.ExactRequirements)
        {
            var step = FindProofStep(assessment, requirement);
            if (step?.Outcome == ProofOutcome.Conflicted)
            {
                return true;
            }
        }

        foreach (var requirement in assessment.ValidationRequirements)
        {
            var check = FindValidationCheck(assessment, requirement);
            if (check?.Outcome == ValidationOutcome.Conflicted)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiredValidationPasses(CorrelationCandidateAssessment assessment)
    {
        foreach (var requirement in assessment.ValidationRequirements)
        {
            var check = FindValidationCheck(assessment, requirement);
            if (check is null || check.Outcome != ValidationOutcome.Passed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ExactProofPasses(CorrelationCandidateAssessment assessment)
    {
        if (assessment.ExactRequirements.Count == 0
            || !assessment.ExactRequirements.Any(static requirement => requirement.EvidenceKind == EvidenceKind.Deterministic))
        {
            return false;
        }

        foreach (var requirement in assessment.ExactRequirements)
        {
            var step = FindProofStep(assessment, requirement);
            if (step is null || step.Outcome != ProofOutcome.Passed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HighConfidenceProofPasses(CorrelationCandidateAssessment assessment)
    {
        if (!assessment.StrongEvidenceSatisfied || assessment.ExactRequirements.Count == 0)
        {
            return false;
        }

        var unavailableAllowedCount = 0;
        foreach (var requirement in assessment.ExactRequirements)
        {
            var step = FindProofStep(assessment, requirement);
            if (step is null)
            {
                return false;
            }

            if (step.Outcome == ProofOutcome.Passed)
            {
                continue;
            }

            if (step.Outcome == ProofOutcome.NotAvailable
                && requirement.EvidenceKind == EvidenceKind.Deterministic
                && requirement.UnavailableForHighConfidenceAllowed)
            {
                unavailableAllowedCount++;
                continue;
            }

            return false;
        }

        return unavailableAllowedCount >= 1;
    }

    private static ProofStepDto? FindProofStep(
        CorrelationCandidateAssessment assessment,
        ProofRequirement requirement)
    {
        return assessment.ProofSteps.FirstOrDefault(step =>
            string.Equals(step.Name, requirement.StepName, StringComparison.Ordinal)
            && step.EvidenceKind == requirement.EvidenceKind);
    }

    private static ValidationCheckDto? FindValidationCheck(
        CorrelationCandidateAssessment assessment,
        ValidationRequirement requirement)
    {
        return assessment.ValidationChecks.FirstOrDefault(check =>
            string.Equals(check.Name, requirement.CheckName, StringComparison.Ordinal));
    }
}
