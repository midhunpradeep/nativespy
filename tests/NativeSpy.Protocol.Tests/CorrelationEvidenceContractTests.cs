using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using Xunit;

namespace NativeSpy.Protocol.Tests;

public sealed class CorrelationEvidenceContractTests
{
    [Fact]
    public void External_evidence_requires_positive_process_identity()
    {
        var source = new ExternalObservationRefDto("observation", "capture");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExternalUiaEvidenceDto(
                source,
                0,
                null,
                Array.Empty<CorrelationEvidenceFactDto>(),
                Array.Empty<CorrelationLimitationDto>()));
    }

    [Fact]
    public void Evidence_collections_are_defensively_copied()
    {
        var facts = new List<CorrelationEvidenceFactDto>
        {
            new("SourceAvailable", ProofOutcome.Passed, EvidenceKind.Deterministic)
        };
        var limitations = new List<CorrelationLimitationDto>
        {
            new("Example")
        };
        var effects = CreateEffects();
        var evidence = new ExternalUiaEvidenceDto(
            new ExternalObservationRefDto("observation", "capture"),
            42,
            123,
            facts,
            limitations);

        facts.Clear();
        limitations.Clear();

        Assert.Single(evidence.EvidenceFacts);
        Assert.Single(evidence.Limitations);

        var framework = new FrameworkCorrelationEvidenceDto(
            "winforms",
            42,
            candidateTarget: null,
            new[] { new CorrelationEvidenceFactDto("ControlFromHandle", ProofOutcome.Failed, EvidenceKind.Deterministic) },
            new[] { new CorrelationValidationFactDto("Current", ValidationOutcome.Changed) },
            effects,
            Array.Empty<AdapterMetadataDto>(),
            Array.Empty<CorrelationLimitationDto>());

        Assert.Single(framework.EvidenceFacts);
        Assert.Single(framework.ValidationFacts);
    }

    [Fact]
    public void Equality_evidence_requires_a_nonzero_numeric_hwnd()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExternalUiaEqualityEvidenceDto(
                new ExternalObservationRefDto("observation", "capture"),
                0,
                Array.Empty<CorrelationEvidenceFactDto>(),
                Array.Empty<CorrelationLimitationDto>()));
    }

    [Fact]
    public void Evidence_facts_reuse_closed_protocol_enums()
    {
        var fact = new CorrelationEvidenceFactDto(
            "ControlFromHandleReferenceEqual",
            ProofOutcome.Failed,
            EvidenceKind.Deterministic);
        var validation = new CorrelationValidationFactDto(
            "CurrentHwndMatches",
            ValidationOutcome.Changed);

        Assert.Equal(ProofOutcome.Failed, fact.Outcome);
        Assert.Equal(EvidenceKind.Deterministic, fact.EvidenceKind);
        Assert.Equal(ValidationOutcome.Changed, validation.Outcome);
    }

    private static CorrelationEffectSummaryDto CreateEffects()
    {
        return new CorrelationEffectSummaryDto(
            new[] { EffectCategory.Passive },
            FrameworkStateEffect.None,
            ApplicationCallbackEffect.None,
            Array.Empty<CallbackDetailDto>(),
            VisibleMutationEffect.NotRequested,
            Array.Empty<string>());
    }
}
