using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Correlation;

internal sealed class WinFormsCurrentHwndNormalizer
{
    private const string CandidateId = "winforms-current-hwnd-1";

    public WinFormsCurrentHwndNormalization Normalize(
        ExternalUiaEvidenceDto externalEvidence,
        FrameworkCorrelationEvidenceDto initialTargetEvidence,
        ExternalUiaEqualityEvidenceDto? initialEqualityEvidence,
        FrameworkCorrelationEvidenceDto? revalidatedTargetEvidence,
        ExternalUiaEqualityEvidenceDto? revalidatedEqualityEvidence,
        CorrelationPolicyDto policy)
    {
        ArgumentNullException.ThrowIfNull(externalEvidence);
        ArgumentNullException.ThrowIfNull(initialTargetEvidence);
        ArgumentNullException.ThrowIfNull(policy);

        var limitations = CollectLimitations(
            externalEvidence,
            initialTargetEvidence,
            initialEqualityEvidence,
            revalidatedTargetEvidence,
            revalidatedEqualityEvidence);
        var operationError = FirstError(
            externalEvidence.OperationError,
            initialTargetEvidence.OperationError,
            initialEqualityEvidence?.OperationError,
            revalidatedTargetEvidence?.OperationError,
            revalidatedEqualityEvidence?.OperationError);

        if (initialEqualityEvidence is not null
            && !SameSource(externalEvidence.Source, initialEqualityEvidence.Source))
        {
            AddLimitation(limitations, new CorrelationLimitationDto("ExternalEvidenceSourceMismatch"));
        }

        if (revalidatedEqualityEvidence is not null
            && !SameSource(externalEvidence.Source, revalidatedEqualityEvidence.Source))
        {
            AddLimitation(limitations, new CorrelationLimitationDto("ExternalEvidenceSourceMismatch"));
        }

        var proofSteps = CreateProofSteps(
            initialTargetEvidence,
            initialEqualityEvidence,
            revalidatedTargetEvidence,
            revalidatedEqualityEvidence);
        var validationRequirements = CreateValidationRequirements();
        var validationChecks = CreateValidationChecks(
            externalEvidence,
            initialTargetEvidence,
            initialEqualityEvidence,
            revalidatedTargetEvidence,
            revalidatedEqualityEvidence);
        var candidateExists = initialTargetEvidence.CandidateTarget is not null;
        var revalidated = candidateExists
            && initialEqualityEvidence is not null
            && revalidatedTargetEvidence is not null
            && revalidatedEqualityEvidence is not null
            && ValidationRequirementsPass(validationChecks, validationRequirements);
        var validation = new CorrelationValidationDto(
            validationChecks,
            Array.Empty<GenerationRefDto>(),
            revalidated);

        CorrelationCandidateDto[] candidates;
        CorrelationCandidateAssessment[] assessments;
        if (initialTargetEvidence.CandidateTarget is null)
        {
            candidates = Array.Empty<CorrelationCandidateDto>();
            assessments = Array.Empty<CorrelationCandidateAssessment>();
        }
        else
        {
            var candidate = CreateCandidate(
                initialTargetEvidence.CandidateTarget,
                externalEvidence,
                initialTargetEvidence,
                initialEqualityEvidence,
                revalidatedTargetEvidence,
                revalidatedEqualityEvidence,
                proofSteps,
                validationChecks,
                validationRequirements,
                revalidated,
                limitations);
            candidates = new[] { candidate };
            assessments = new[]
            {
                new CorrelationCandidateAssessment(
                    candidate,
                    proofSteps,
                    validationChecks,
                    CreateExactRequirements(),
                    validationRequirements,
                    strongEvidenceSatisfied: false)
            };
        }

        var proofSummary = proofSteps.Count == 0 && candidates.Length == 0
            ? null
            : new CorrelationProofSummaryDto(
                CorrelationProofMethod.WinFormsCurrentHwnd,
                proofSteps,
                validationChecks,
                limitations,
                revalidated);

        return new WinFormsCurrentHwndNormalization(
            candidates,
            assessments,
            proofSummary,
            validation,
            MergeEffects(
                initialTargetEvidence,
                initialEqualityEvidence,
                revalidatedTargetEvidence,
                revalidatedEqualityEvidence),
            limitations,
            operationError);
    }

    private static CorrelationCandidateDto CreateCandidate(
        CorrelationTargetRefDto target,
        ExternalUiaEvidenceDto externalEvidence,
        FrameworkCorrelationEvidenceDto initialTargetEvidence,
        ExternalUiaEqualityEvidenceDto? initialEqualityEvidence,
        FrameworkCorrelationEvidenceDto? revalidatedTargetEvidence,
        ExternalUiaEqualityEvidenceDto? revalidatedEqualityEvidence,
        IReadOnlyList<ProofStepDto> proofSteps,
        IReadOnlyList<ValidationCheckDto> validationChecks,
        IReadOnlyList<ValidationRequirement> validationRequirements,
        bool revalidated,
        IReadOnlyList<CorrelationLimitationDto> limitations)
    {
        var exactFactsPass = ExactFactsPass(proofSteps);
        var validationPass = ValidationRequirementsPass(validationChecks, validationRequirements);
        var relationships = exactFactsPass && validationPass && revalidated
            ? new[]
            {
                new RelationshipClaimDto(
                    RelationshipKind.SameManagedElement,
                    target,
                    RelationshipScope.CurrentObservation)
            }
            : Array.Empty<RelationshipClaimDto>();

        return new CorrelationCandidateDto(
            CandidateId,
            target,
            relationships,
            CreateEvidenceSummaries(
                externalEvidence,
                initialTargetEvidence,
                initialEqualityEvidence,
                revalidatedTargetEvidence,
                revalidatedEqualityEvidence),
            new CorrelationProofSummaryDto(
                CorrelationProofMethod.WinFormsCurrentHwnd,
                proofSteps,
                validationChecks,
                limitations,
                revalidated),
            new CorrelationValidationDto(
                validationChecks,
                Array.Empty<GenerationRefDto>(),
                revalidated),
            MergeEffects(
                initialTargetEvidence,
                initialEqualityEvidence,
                revalidatedTargetEvidence,
                revalidatedEqualityEvidence),
            limitations);
    }

    private static IReadOnlyList<ProofStepDto> CreateProofSteps(
        FrameworkCorrelationEvidenceDto initialTargetEvidence,
        ExternalUiaEqualityEvidenceDto? initialEqualityEvidence,
        FrameworkCorrelationEvidenceDto? revalidatedTargetEvidence,
        ExternalUiaEqualityEvidenceDto? revalidatedEqualityEvidence)
    {
        var steps = new List<ProofStepDto>();
        AddNormalizedEqualityProofFact(
            steps,
            initialEqualityEvidence,
            "ElementFromHandle",
            "ElementFromHandle");
        AddNormalizedEqualityProofFact(
            steps,
            initialEqualityEvidence,
            "CompareElements",
            "CompareElements");
        AddProofFact(steps, initialTargetEvidence, "ControlFromHandle");
        AddProofFact(steps, revalidatedTargetEvidence, "ControlFromHandleReferenceEqual");
        AddNormalizedEqualityProofFact(
            steps,
            revalidatedEqualityEvidence,
            "ElementFromHandle",
            "ElementFromHandleRevalidated");
        AddNormalizedEqualityProofFact(
            steps,
            revalidatedEqualityEvidence,
            "CompareElements",
            "CompareElementsRevalidated");
        return steps;
    }

    private static IReadOnlyList<ProofRequirement> CreateExactRequirements()
    {
        return new[]
        {
            new ProofRequirement("ElementFromHandle", EvidenceKind.Deterministic),
            new ProofRequirement("CompareElements", EvidenceKind.Deterministic),
            new ProofRequirement("ControlFromHandle", EvidenceKind.Deterministic),
            new ProofRequirement("ControlFromHandleReferenceEqual", EvidenceKind.Deterministic),
            new ProofRequirement("ElementFromHandleRevalidated", EvidenceKind.Deterministic),
            new ProofRequirement("CompareElementsRevalidated", EvidenceKind.Deterministic)
        };
    }

    private static IReadOnlyList<ValidationRequirement> CreateValidationRequirements()
    {
        return new[]
        {
            new ValidationRequirement("ProcessIdentityCurrent"),
            new ValidationRequirement("RevalidatedProcessIdentityCurrent"),
            new ValidationRequirement("InitialExternalHwndMatches"),
            new ValidationRequirement("RevalidatedExternalHwndMatches"),
            new ValidationRequirement("InitialExternalSourceCurrent"),
            new ValidationRequirement("RevalidatedExternalSourceCurrent"),
            new ValidationRequirement("InitialCurrentHwndMatches"),
            new ValidationRequirement("InitialControlLive"),
            new ValidationRequirement("RevalidatedCurrentHwndMatches"),
            new ValidationRequirement("RevalidatedControlLive"),
            new ValidationRequirement("CandidateReferenceResolved")
        };
    }

    private static IReadOnlyList<ValidationCheckDto> CreateValidationChecks(
        ExternalUiaEvidenceDto externalEvidence,
        FrameworkCorrelationEvidenceDto initialTargetEvidence,
        ExternalUiaEqualityEvidenceDto? initialEqualityEvidence,
        FrameworkCorrelationEvidenceDto? revalidatedTargetEvidence,
        ExternalUiaEqualityEvidenceDto? revalidatedEqualityEvidence)
    {
        var externalProcessFact = FindFact(externalEvidence.EvidenceFacts, "ExternalElementProcessIdentity");
        var processIdentity = externalEvidence.ProcessId == initialTargetEvidence.ProcessId
            && externalProcessFact is not null
            && externalProcessFact.EvidenceKind == EvidenceKind.Deterministic
            && externalProcessFact.Outcome == ProofOutcome.Passed
            ? ValidationOutcome.Passed
            : externalProcessFact?.Outcome == ProofOutcome.NotAvailable
                ? ValidationOutcome.NotAvailable
                : ValidationOutcome.Failed;
        var initialCurrent = ValidationFromFact(initialTargetEvidence, "CurrentHwndMatches");
        var initialLive = ValidationFromFact(initialTargetEvidence, "ControlLive");
        var revalidatedProcess = RevalidatedProcessValidation(
            externalEvidence,
            revalidatedTargetEvidence);
        var initialExternalHwnd = initialEqualityEvidence is null
            ? ValidationOutcome.NotAvailable
            : EqualityObservationValidation(externalEvidence, initialEqualityEvidence);
        var revalidatedExternalHwnd = revalidatedEqualityEvidence is null
            ? ValidationOutcome.NotAvailable
            : EqualityObservationValidation(externalEvidence, revalidatedEqualityEvidence);
        var revalidatedCurrent = RevalidatedValidation(initialCurrent, revalidatedTargetEvidence, "CurrentHwndMatches");
        var revalidatedLive = RevalidatedValidation(initialLive, revalidatedTargetEvidence, "ControlLive");
        var candidateResolved = ValidationFromFact(revalidatedTargetEvidence, "CandidateResolved");
        var requiredChecks = new[]
        {
            new ValidationCheckDto("ProcessIdentityCurrent", processIdentity),
            new ValidationCheckDto("RevalidatedProcessIdentityCurrent", revalidatedProcess),
            new ValidationCheckDto("InitialExternalHwndMatches", initialExternalHwnd),
            new ValidationCheckDto("RevalidatedExternalHwndMatches", revalidatedExternalHwnd),
            new ValidationCheckDto("InitialExternalSourceCurrent", initialEqualityEvidence is null
                ? ValidationOutcome.NotAvailable
                : EqualitySourceValidation(initialEqualityEvidence)),
            new ValidationCheckDto("RevalidatedExternalSourceCurrent", revalidatedEqualityEvidence is null
                ? ValidationOutcome.NotAvailable
                : EqualitySourceValidation(revalidatedEqualityEvidence)),
            new ValidationCheckDto("InitialCurrentHwndMatches", initialCurrent),
            new ValidationCheckDto("InitialControlLive", initialLive),
            new ValidationCheckDto("RevalidatedCurrentHwndMatches", revalidatedCurrent),
            new ValidationCheckDto("RevalidatedControlLive", revalidatedLive),
            new ValidationCheckDto("CandidateReferenceResolved", candidateResolved)
        };
        var reservedNames = new HashSet<string>(
            requiredChecks.Select(static check => check.Name),
            StringComparer.Ordinal);
        var diagnosticFacts = new List<CorrelationValidationFactDto>();
        diagnosticFacts.AddRange(initialTargetEvidence.ValidationFacts);
        if (revalidatedTargetEvidence is not null)
        {
            diagnosticFacts.AddRange(revalidatedTargetEvidence.ValidationFacts);
        }

        return requiredChecks
            .Concat(CreateDiagnosticValidationChecks(diagnosticFacts, reservedNames))
            .ToArray();
    }

    private static ValidationOutcome RevalidatedValidation(
        ValidationOutcome initial,
        FrameworkCorrelationEvidenceDto? revalidated,
        string factName)
    {
        var second = ValidationFromFact(revalidated, factName);
        if (initial == ValidationOutcome.Passed && second == ValidationOutcome.Passed)
        {
            return ValidationOutcome.Passed;
        }

        if (initial == ValidationOutcome.Passed
            && second is ValidationOutcome.Failed or ValidationOutcome.Changed)
        {
            return ValidationOutcome.Changed;
        }

        return second;
    }

    private static ValidationOutcome RevalidatedProcessValidation(
        ExternalUiaEvidenceDto externalEvidence,
        FrameworkCorrelationEvidenceDto? revalidatedTargetEvidence)
    {
        if (revalidatedTargetEvidence is null)
        {
            return ValidationOutcome.NotAvailable;
        }

        return externalEvidence.ProcessId == revalidatedTargetEvidence.ProcessId
            ? ValidationOutcome.Passed
            : ValidationOutcome.Failed;
    }

    private static ValidationOutcome EqualityObservationValidation(
        ExternalUiaEvidenceDto externalEvidence,
        ExternalUiaEqualityEvidenceDto equalityEvidence)
    {
        if (externalEvidence.ObservedHwnd is null
            || externalEvidence.ObservedHwnd.Value != equalityEvidence.Hwnd
            || !SameSource(externalEvidence.Source, equalityEvidence.Source))
        {
            return ValidationOutcome.Failed;
        }

        return EqualitySourceValidation(equalityEvidence);
    }

    private static ValidationOutcome EqualitySourceValidation(ExternalUiaEqualityEvidenceDto evidence)
    {
        return FindFact(evidence.EvidenceFacts, "SourceAvailable")?.Outcome switch
        {
            ProofOutcome.Passed => ValidationOutcome.Passed,
            ProofOutcome.Failed => ValidationOutcome.Failed,
            ProofOutcome.NotAvailable or ProofOutcome.NotAttempted => ValidationOutcome.NotAvailable,
            ProofOutcome.Conflicted => ValidationOutcome.Conflicted,
            _ => ValidationOutcome.NotAvailable
        };
    }

    private static ValidationOutcome ValidationFromFact(
        FrameworkCorrelationEvidenceDto? evidence,
        string name)
    {
        if (evidence is null)
        {
            return ValidationOutcome.NotAvailable;
        }

        return evidence.ValidationFacts.FirstOrDefault(
            fact => string.Equals(fact.Name, name, StringComparison.Ordinal))?.Outcome
            ?? ValidationOutcome.NotAvailable;
    }

    private static IReadOnlyList<CorrelationEvidenceSummaryDto> CreateEvidenceSummaries(
        ExternalUiaEvidenceDto externalEvidence,
        FrameworkCorrelationEvidenceDto initialTargetEvidence,
        ExternalUiaEqualityEvidenceDto? initialEqualityEvidence,
        FrameworkCorrelationEvidenceDto? revalidatedTargetEvidence,
        ExternalUiaEqualityEvidenceDto? revalidatedEqualityEvidence)
    {
        var summaries = new List<CorrelationEvidenceSummaryDto>();
        AddSummaries(summaries, externalEvidence.EvidenceFacts);
        AddSummaries(summaries, initialTargetEvidence.EvidenceFacts);
        AddSummaries(summaries, initialEqualityEvidence?.EvidenceFacts);
        AddSummaries(summaries, revalidatedTargetEvidence?.EvidenceFacts);
        AddSummaries(summaries, revalidatedEqualityEvidence?.EvidenceFacts);
        return summaries;
    }

    private static CorrelationEffectSummaryDto MergeEffects(
        FrameworkCorrelationEvidenceDto initialTargetEvidence,
        ExternalUiaEqualityEvidenceDto? initialEqualityEvidence,
        FrameworkCorrelationEvidenceDto? revalidatedTargetEvidence,
        ExternalUiaEqualityEvidenceDto? revalidatedEqualityEvidence)
    {
        var categories = new List<EffectCategory> { EffectCategory.Passive };
        var operations = new List<string> { "FlaUI.Capture" };
        if (initialEqualityEvidence is not null)
        {
            operations.Add("FlaUI.ElementFromHandle");
            operations.Add("FlaUI.CompareElements");
        }

        if (revalidatedEqualityEvidence is not null)
        {
            operations.Add("FlaUI.ElementFromHandle.Revalidate");
            operations.Add("FlaUI.CompareElements.Revalidate");
        }

        operations.Add("WinForms.Control.FromHandle");
        if (revalidatedTargetEvidence is not null)
        {
            operations.Add("WinForms.Control.FromHandle.Revalidate");
        }
        var frameworkState = FrameworkStateEffect.None;
        var callbacks = ApplicationCallbackEffect.None;
        var mutation = VisibleMutationEffect.NotRequested;

        foreach (var evidence in new[] { initialTargetEvidence, revalidatedTargetEvidence })
        {
            if (evidence is null)
            {
                continue;
            }

            foreach (var category in evidence.Effects.Categories)
            {
                if (!categories.Contains(category))
                {
                    categories.Add(category);
                }
            }

            frameworkState = Stronger(frameworkState, evidence.Effects.FrameworkState);
            callbacks = Stronger(callbacks, evidence.Effects.ApplicationCallbacks);
            mutation = Stronger(mutation, evidence.Effects.VisibleMutation);
            operations.AddRange(evidence.Effects.Operations);
        }

        return new CorrelationEffectSummaryDto(
            categories,
            frameworkState,
            callbacks,
            Array.Empty<CallbackDetailDto>(),
            mutation,
            operations.Distinct(StringComparer.Ordinal));
    }

    private static FrameworkStateEffect Stronger(FrameworkStateEffect first, FrameworkStateEffect second)
    {
        if (first == FrameworkStateEffect.Unknown || second == FrameworkStateEffect.Unknown)
        {
            return FrameworkStateEffect.Unknown;
        }

        if (first == FrameworkStateEffect.Possible || second == FrameworkStateEffect.Possible)
        {
            return FrameworkStateEffect.Possible;
        }

        if (first == FrameworkStateEffect.Observed || second == FrameworkStateEffect.Observed)
        {
            return FrameworkStateEffect.Observed;
        }

        return FrameworkStateEffect.None;
    }

    private static ApplicationCallbackEffect Stronger(
        ApplicationCallbackEffect first,
        ApplicationCallbackEffect second)
    {
        if (first == ApplicationCallbackEffect.Unknown || second == ApplicationCallbackEffect.Unknown)
        {
            return ApplicationCallbackEffect.Unknown;
        }

        return first == ApplicationCallbackEffect.Observed || second == ApplicationCallbackEffect.Observed
            ? ApplicationCallbackEffect.Observed
            : ApplicationCallbackEffect.None;
    }

    private static VisibleMutationEffect Stronger(
        VisibleMutationEffect first,
        VisibleMutationEffect second)
    {
        if (first == VisibleMutationEffect.Unknown || second == VisibleMutationEffect.Unknown)
        {
            return VisibleMutationEffect.Unknown;
        }

        return first == VisibleMutationEffect.Observed || second == VisibleMutationEffect.Observed
            ? VisibleMutationEffect.Observed
            : VisibleMutationEffect.NotRequested;
    }

    private static bool ExactFactsPass(IReadOnlyList<ProofStepDto> proofSteps)
    {
        var names = new[]
        {
            "ElementFromHandle",
            "CompareElements",
            "ControlFromHandle",
            "ControlFromHandleReferenceEqual",
            "ElementFromHandleRevalidated",
            "CompareElementsRevalidated"
        };
        return names.All(name => proofSteps.Any(step =>
            string.Equals(step.Name, name, StringComparison.Ordinal)
            && step.EvidenceKind == EvidenceKind.Deterministic
            && step.Outcome == ProofOutcome.Passed));
    }

    private static IReadOnlyList<ValidationCheckDto> CreateDiagnosticValidationChecks(
        IEnumerable<CorrelationValidationFactDto> facts,
        IReadOnlySet<string> reservedNames)
    {
        return facts
            .Where(fact => !reservedNames.Contains(fact.Name))
            .GroupBy(fact => fact.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var distinctFacts = group
                    .Select(fact => (fact.Outcome, fact.Detail))
                    .Distinct()
                    .ToArray();
                return distinctFacts.Length == 1
                    ? new ValidationCheckDto(
                        group.Key,
                        distinctFacts[0].Outcome,
                        distinctFacts[0].Detail)
                    : new ValidationCheckDto(
                        group.Key,
                        ValidationOutcome.Conflicted,
                        "Duplicate diagnostic validation facts conflicted.");
            })
            .ToArray();
    }

    private static bool ValidationRequirementsPass(
        IReadOnlyList<ValidationCheckDto> checks,
        IReadOnlyList<ValidationRequirement> requirements)
    {
        return requirements.All(requirement => checks.Any(check =>
            string.Equals(check.Name, requirement.CheckName, StringComparison.Ordinal)
            && check.Outcome == ValidationOutcome.Passed));
    }

    private static void AddNormalizedEqualityProofFact(
        List<ProofStepDto> target,
        ExternalUiaEqualityEvidenceDto? evidence,
        string adapterFactName,
        string normalizedName)
    {
        var fact = evidence is null ? null : FindFact(evidence.EvidenceFacts, adapterFactName);
        if (fact is not null)
        {
            target.Add(new ProofStepDto(normalizedName, fact.Outcome, fact.EvidenceKind, fact.Detail));
        }
    }

    private static void AddProofFact(
        List<ProofStepDto> target,
        FrameworkCorrelationEvidenceDto? evidence,
        string name)
    {
        var fact = evidence is null ? null : FindFact(evidence.EvidenceFacts, name);
        if (fact is not null)
        {
            target.Add(new ProofStepDto(fact.Name, fact.Outcome, fact.EvidenceKind, fact.Detail));
        }
    }

    private static CorrelationEvidenceFactDto? FindFact(
        IEnumerable<CorrelationEvidenceFactDto> facts,
        string name)
    {
        return facts.FirstOrDefault(fact => string.Equals(fact.Name, name, StringComparison.Ordinal));
    }

    private static void AddSummaries(
        List<CorrelationEvidenceSummaryDto> target,
        IEnumerable<CorrelationEvidenceFactDto>? facts)
    {
        if (facts is null)
        {
            return;
        }

        target.AddRange(facts.Select(fact => new CorrelationEvidenceSummaryDto(
            fact.Name,
            fact.EvidenceKind,
            fact.Detail ?? fact.Outcome.ToString())));
    }

    private static List<CorrelationLimitationDto> CollectLimitations(
        ExternalUiaEvidenceDto externalEvidence,
        FrameworkCorrelationEvidenceDto initialTargetEvidence,
        ExternalUiaEqualityEvidenceDto? initialEqualityEvidence,
        FrameworkCorrelationEvidenceDto? revalidatedTargetEvidence,
        ExternalUiaEqualityEvidenceDto? revalidatedEqualityEvidence)
    {
        var result = new List<CorrelationLimitationDto>();
        AddLimitations(result, externalEvidence.Limitations);
        AddLimitations(result, initialTargetEvidence.Limitations);
        AddLimitations(result, initialEqualityEvidence?.Limitations);
        AddLimitations(result, revalidatedTargetEvidence?.Limitations);
        AddLimitations(result, revalidatedEqualityEvidence?.Limitations);
        return result;
    }

    private static void AddLimitations(
        List<CorrelationLimitationDto> target,
        IEnumerable<CorrelationLimitationDto>? limitations)
    {
        if (limitations is null)
        {
            return;
        }

        foreach (var limitation in limitations)
        {
            AddLimitation(target, limitation);
        }
    }

    private static void AddLimitation(
        List<CorrelationLimitationDto> target,
        CorrelationLimitationDto limitation)
    {
        if (target.All(existing => !string.Equals(existing.Code, limitation.Code, StringComparison.Ordinal)))
        {
            target.Add(limitation);
        }
    }

    private static OperationErrorDto? FirstError(params OperationErrorDto?[] errors)
    {
        return errors.FirstOrDefault(error => error is not null);
    }

    private static bool SameSource(
        ExternalUiaCaptureRefDto expected,
        ExternalUiaCaptureRefDto actual)
    {
        return string.Equals(expected.ObservationId, actual.ObservationId, StringComparison.Ordinal)
            && string.Equals(expected.CaptureId, actual.CaptureId, StringComparison.Ordinal);
    }
}
