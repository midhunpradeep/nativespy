using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Correlation;

/// <summary>
/// Narrow I1 coordinator for external UIA to WinForms current-HWND correlation.
/// </summary>
public sealed class CorrelationCoordinator
{
    private readonly CorrelationEvaluator _evaluator = new();
    private readonly WinFormsCurrentHwndNormalizer _normalizer = new();

    public async Task<CorrelationResultDto> ResolveUiaToWinFormsAsync(
        IExternalUiaObservationPort externalPort,
        IWinFormsCorrelationPort targetPort,
        CorrelationPolicyDto policy,
        CancellationToken cancellationToken = default)
    {
        if (externalPort is null)
        {
            throw new ArgumentNullException(nameof(externalPort));
        }

        if (targetPort is null)
        {
            throw new ArgumentNullException(nameof(targetPort));
        }

        if (policy is null)
        {
            throw new ArgumentNullException(nameof(policy));
        }

        if (policy.Mode != CorrelationPolicyMode.Conservative)
        {
            throw new NotSupportedException("I1 supports only Conservative correlation policy.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var externalEvidence = await externalPort.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var source = new CorrelationSourceDto(
            CorrelationSourceKind.ExternalObservation,
            externalObservation: ToLegacyObservationReference(externalEvidence.Source));

        if (externalEvidence.OperationError is not null)
        {
            return CreateUnresolvedResult(
                source,
                policy,
                externalEvidence.OperationError,
                new[] { new CorrelationLimitationDto("ExternalCaptureFailed") });
        }

        if (!EvidencePassed(externalEvidence, "SourceAvailable")
            || !EvidencePassed(externalEvidence, "CurrentHwndObserved"))
        {
            return CreateUnresolvedResult(
                source,
                policy,
                operationError: null,
                new[] { new CorrelationLimitationDto("ExternalSourceUnavailableOrIncomplete") });
        }

        if (!EvidencePassed(externalEvidence, "ExternalElementProcessIdentity"))
        {
            return CreateUnresolvedResult(
                source,
                policy,
                operationError: null,
                new[] { new CorrelationLimitationDto("ExternalProcessIdentityUnavailableOrMismatched") });
        }

        if (externalEvidence.ObservedHwnd is null)
        {
            return CreateUnresolvedResult(
                source,
                policy,
                operationError: null,
                new[] { new CorrelationLimitationDto("NoUsableCurrentHwnd") });
        }

        var hwnd = externalEvidence.ObservedHwnd.Value;
        cancellationToken.ThrowIfCancellationRequested();
        var initialTargetEvidence = await targetPort
            .BeginCurrentHwndAsync(hwnd, cancellationToken)
            .ConfigureAwait(false);

        if (initialTargetEvidence.OperationError is not null)
        {
            return CreateResultFromEvidence(
                source,
                policy,
                externalEvidence,
                initialTargetEvidence,
                initialEqualityEvidence: null,
                revalidatedTargetEvidence: null,
                revalidatedEqualityEvidence: null,
                operationError: initialTargetEvidence.OperationError,
                extraLimitations: new[] { new CorrelationLimitationDto("InitialTargetOperationFailed") });
        }

        if (externalEvidence.ProcessId != initialTargetEvidence.ProcessId)
        {
            return CreateResultFromEvidence(
                source,
                policy,
                externalEvidence,
                initialTargetEvidence,
                initialEqualityEvidence: null,
                revalidatedTargetEvidence: null,
                revalidatedEqualityEvidence: null,
                operationError: null,
                extraLimitations: new[] { new CorrelationLimitationDto("ProcessIdentityMismatch") });
        }

        cancellationToken.ThrowIfCancellationRequested();
        var initialEqualityEvidence = await externalPort
            .CompareWithHwndAsync(externalEvidence.Source, hwnd, cancellationToken)
            .ConfigureAwait(false);

        if (initialEqualityEvidence.OperationError is not null
            || !EvidencePassed(initialEqualityEvidence, "SourceAvailable")
            || !EvidencePassed(initialEqualityEvidence, "ElementFromHandle")
            || !EvidencePassed(initialEqualityEvidence, "CompareElements")
            || !MatchesExternalObservation(externalEvidence, initialEqualityEvidence, hwnd))
        {
            return CreateResultFromEvidence(
                source,
                policy,
                externalEvidence,
                initialTargetEvidence,
                initialEqualityEvidence,
                revalidatedTargetEvidence: null,
                revalidatedEqualityEvidence: null,
                operationError: initialEqualityEvidence.OperationError,
                extraLimitations: initialEqualityEvidence.OperationError is null
                    ? new[]
                    {
                        new CorrelationLimitationDto(
                            MatchesExternalObservation(externalEvidence, initialEqualityEvidence, hwnd)
                                ? "InitialExternalEqualityFailed"
                                : "InitialExternalEvidenceMismatch")
                    }
                    : new[] { new CorrelationLimitationDto("InitialExternalEqualityOperationFailed") });
        }

        var candidateHandle = ExtractCandidateHandle(initialTargetEvidence.CandidateTarget);
        if (candidateHandle is null)
        {
            return CreateResultFromEvidence(
                source,
                policy,
                externalEvidence,
                initialTargetEvidence,
                initialEqualityEvidence,
                revalidatedTargetEvidence: null,
                revalidatedEqualityEvidence: null,
                operationError: null,
                extraLimitations: new[] { new CorrelationLimitationDto("NoManagedCandidate") });
        }

        cancellationToken.ThrowIfCancellationRequested();
        var revalidatedTargetEvidence = await targetPort
            .RevalidateCurrentHwndAsync(hwnd, candidateHandle, cancellationToken)
            .ConfigureAwait(false);

        if (revalidatedTargetEvidence.OperationError is not null
            || revalidatedTargetEvidence.ProcessId != externalEvidence.ProcessId
            || !EvidencePassed(revalidatedTargetEvidence, "ControlFromHandleReferenceEqual")
            || !ValidationPassed(revalidatedTargetEvidence, "CurrentHwndMatches")
            || !ValidationPassed(revalidatedTargetEvidence, "ControlLive"))
        {
            return CreateResultFromEvidence(
                source,
                policy,
                externalEvidence,
                initialTargetEvidence,
                initialEqualityEvidence,
                revalidatedTargetEvidence,
                revalidatedEqualityEvidence: null,
                operationError: revalidatedTargetEvidence.OperationError,
                extraLimitations: revalidatedTargetEvidence.OperationError is null
                    ? new[] { new CorrelationLimitationDto("TargetRevalidationFailed") }
                    : new[] { new CorrelationLimitationDto("TargetRevalidationOperationFailed") });
        }

        cancellationToken.ThrowIfCancellationRequested();
        var revalidatedEqualityEvidence = await externalPort
            .CompareWithHwndAsync(externalEvidence.Source, hwnd, cancellationToken)
            .ConfigureAwait(false);

        var revalidatedEqualityPassed = revalidatedEqualityEvidence.OperationError is null
            && EvidencePassed(revalidatedEqualityEvidence, "SourceAvailable")
            && EvidencePassed(revalidatedEqualityEvidence, "ElementFromHandle")
            && EvidencePassed(revalidatedEqualityEvidence, "CompareElements")
            && MatchesExternalObservation(externalEvidence, revalidatedEqualityEvidence, hwnd);
        return CreateResultFromEvidence(
            source,
            policy,
            externalEvidence,
            initialTargetEvidence,
            initialEqualityEvidence,
            revalidatedTargetEvidence,
            revalidatedEqualityEvidence,
            revalidatedEqualityEvidence.OperationError,
            revalidatedEqualityPassed
                ? Array.Empty<CorrelationLimitationDto>()
                : new[]
                {
                    new CorrelationLimitationDto(
                        MatchesExternalObservation(externalEvidence, revalidatedEqualityEvidence, hwnd)
                            ? "ExternalEqualityRevalidationFailed"
                            : "ExternalRevalidationEvidenceMismatch")
                });
    }

    private CorrelationResultDto CreateResultFromEvidence(
        CorrelationSourceDto source,
        CorrelationPolicyDto policy,
        ExternalUiaEvidenceDto externalEvidence,
        FrameworkCorrelationEvidenceDto initialTargetEvidence,
        ExternalUiaEqualityEvidenceDto? initialEqualityEvidence,
        FrameworkCorrelationEvidenceDto? revalidatedTargetEvidence,
        ExternalUiaEqualityEvidenceDto? revalidatedEqualityEvidence,
        OperationErrorDto? operationError,
        IEnumerable<CorrelationLimitationDto> extraLimitations)
    {
        var normalization = _normalizer.Normalize(
            externalEvidence,
            initialTargetEvidence,
            initialEqualityEvidence,
            revalidatedTargetEvidence,
            revalidatedEqualityEvidence,
            policy);
        var limitations = normalization.Limitations
            .Concat(extraLimitations)
            .GroupBy(limitation => limitation.Code, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var assessments = normalization.Assessments;
        var decision = assessments.Count == 0
            ? new CorrelationEvaluationDecision(
                CorrelationStatus.Unresolved,
                primaryCandidateId: null,
                CorrelationDecisionReason.NoCandidates)
            : _evaluator.Evaluate(new CorrelationEvaluationInput(
                assessments,
                policy));

        return new CorrelationResultDto(
            decision.Status,
            CorrelationDirection.UiaToNative,
            policy,
            source,
            normalization.Candidates,
            decision.PrimaryCandidateId,
            normalization.ProofSummary,
            normalization.Effects,
            normalization.Validation,
            Array.Empty<CorrelationConflictDto>(),
            limitations,
            deeperProofAvailable: false,
            requiredPolicy: null,
            operationError ?? normalization.OperationError);
    }

    private static CorrelationResultDto CreateUnresolvedResult(
        CorrelationSourceDto source,
        CorrelationPolicyDto policy,
        OperationErrorDto? operationError,
        IEnumerable<CorrelationLimitationDto> limitations)
    {
        var effects = new CorrelationEffectSummaryDto(
            new[] { EffectCategory.Passive },
            FrameworkStateEffect.None,
            ApplicationCallbackEffect.None,
            Array.Empty<CallbackDetailDto>(),
            VisibleMutationEffect.NotRequested,
            new[] { "FlaUI.Capture" });
        return new CorrelationResultDto(
            CorrelationStatus.Unresolved,
            CorrelationDirection.UiaToNative,
            policy,
            source,
            Array.Empty<CorrelationCandidateDto>(),
            primaryCandidateId: null,
            proofSummary: null,
            effects,
            new CorrelationValidationDto(
                Array.Empty<ValidationCheckDto>(),
                Array.Empty<GenerationRefDto>(),
                revalidated: false),
            Array.Empty<CorrelationConflictDto>(),
            limitations,
            deeperProofAvailable: false,
            requiredPolicy: null,
            operationError);
    }

    private static HandleRefDto? ExtractCandidateHandle(CorrelationTargetRefDto? target)
    {
        return target?.TargetKind == CorrelationTargetKind.ManagedObject
            ? target.Managed?.Handle
            : null;
    }

    private static ExternalObservationRefDto ToLegacyObservationReference(
        ExternalUiaCaptureRefDto capture)
    {
        return new ExternalObservationRefDto(capture.ObservationId, capture.CaptureId);
    }

    private static bool MatchesExternalObservation(
        ExternalUiaEvidenceDto externalEvidence,
        ExternalUiaEqualityEvidenceDto equalityEvidence,
        ulong hwnd)
    {
        return equalityEvidence.Hwnd == hwnd
            && string.Equals(
                externalEvidence.Source.ObservationId,
                equalityEvidence.Source.ObservationId,
                StringComparison.Ordinal)
            && string.Equals(
                externalEvidence.Source.CaptureId,
                equalityEvidence.Source.CaptureId,
                StringComparison.Ordinal);
    }

    private static bool EvidencePassed(
        ExternalUiaEvidenceDto evidence,
        string name)
    {
        return evidence.EvidenceFacts.Any(fact =>
            string.Equals(fact.Name, name, StringComparison.Ordinal)
            && fact.EvidenceKind == EvidenceKind.Deterministic
            && fact.Outcome == ProofOutcome.Passed);
    }

    private static bool EvidencePassed(
        ExternalUiaEqualityEvidenceDto evidence,
        string name)
    {
        return evidence.EvidenceFacts.Any(fact =>
            string.Equals(fact.Name, name, StringComparison.Ordinal)
            && fact.EvidenceKind == EvidenceKind.Deterministic
            && fact.Outcome == ProofOutcome.Passed);
    }

    private static bool EvidencePassed(
        FrameworkCorrelationEvidenceDto evidence,
        string name)
    {
        return evidence.EvidenceFacts.Any(fact =>
            string.Equals(fact.Name, name, StringComparison.Ordinal)
            && fact.EvidenceKind == EvidenceKind.Deterministic
            && fact.Outcome == ProofOutcome.Passed);
    }

    private static bool ValidationPassed(
        FrameworkCorrelationEvidenceDto evidence,
        string name)
    {
        return evidence.ValidationFacts.Any(fact =>
            string.Equals(fact.Name, name, StringComparison.Ordinal)
            && fact.Outcome == ValidationOutcome.Passed);
    }
}
