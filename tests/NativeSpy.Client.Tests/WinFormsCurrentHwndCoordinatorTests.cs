using NativeSpy.Client.Correlation;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using Xunit;

namespace NativeSpy.Client.Tests;

public sealed class WinFormsCurrentHwndCoordinatorTests
{
    private static readonly CorrelationPolicyDto Policy = new(
        CorrelationPolicyMode.Conservative,
        maxCandidates: 1,
        maxExternalNodes: 2_000,
        maxProviderNodes: 32);

    [Fact]
    public async Task Unsupported_policy_is_rejected_before_adapter_calls()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, 123));
        var target = new FakeTargetPort(CreateInitialTarget(1234));
        var providerAware = new CorrelationPolicyDto(
            CorrelationPolicyMode.ProviderAware,
            maxCandidates: 1,
            maxExternalNodes: 2_000,
            maxProviderNodes: 32);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new CorrelationCoordinator().ResolveUiaToWinFormsAsync(
                external,
                target,
                providerAware,
                CancellationToken.None));
        Assert.Equal(0, target.BeginCalls);
    }

    [Fact]
    public async Task Complete_current_hwnd_facts_are_exact_and_same_managed_element()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, 123));
        external.EqualityEvidence.Enqueue(CreateEquality(123, comparePassed: true));
        external.EqualityEvidence.Enqueue(CreateEquality(123, comparePassed: true));
        var target = new FakeTargetPort(CreateInitialTarget(1234));
        target.Revalidated = CreateRevalidatedTarget(1234, referenceEqual: true, current: true, live: true);

        var result = await Resolve(external, target);

        Assert.Equal(CorrelationStatus.Exact, result.Status);
        Assert.Single(result.Candidates);
        Assert.Equal(result.Candidates[0].CandidateId, result.PrimaryCandidateId);
        Assert.Contains(
            result.Candidates[0].Relationships,
            relationship => relationship.Kind == RelationshipKind.SameManagedElement);
        Assert.True(result.Validation.Revalidated);
        Assert.Contains(
            result.Candidates[0].ProofSummary!.Steps,
            step => step.Name == "ElementFromHandleRevalidated");
        Assert.Contains(
            result.Candidates[0].ProofSummary!.Steps,
            step => step.Name == "CompareElementsRevalidated");
    }

    [Fact]
    public async Task Unrelated_diagnostic_validation_failure_does_not_block_exact()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, 123));
        external.EqualityEvidence.Enqueue(CreateEquality(123, comparePassed: true));
        external.EqualityEvidence.Enqueue(CreateEquality(123, comparePassed: true));
        var target = new FakeTargetPort(CreateInitialTarget(
            1234,
            CreateTarget(),
            new[] { Validation("DiagnosticProbe", ValidationOutcome.Failed) }));
        target.Revalidated = CreateRevalidatedTarget(
            1234,
            referenceEqual: true,
            current: true,
            live: true,
            diagnosticFacts: new[] { Validation("DiagnosticProbe", ValidationOutcome.Passed) });

        var result = await Resolve(external, target);

        Assert.Equal(CorrelationStatus.Exact, result.Status);
        Assert.True(result.Validation.Revalidated);
        Assert.Contains(
            result.Validation.Checks,
            check => check.Name == "DiagnosticProbe" && check.Outcome == ValidationOutcome.Conflicted);
    }

    [Fact]
    public async Task Required_initial_external_source_currentness_failure_blocks_exact()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, 123));
        external.EqualityEvidence.Enqueue(CreateEquality(
            123,
            comparePassed: true,
            sourceAvailable: ProofOutcome.Failed));
        var target = new FakeTargetPort(CreateInitialTarget(1234));

        var result = await Resolve(external, target);

        Assert.Equal(CorrelationStatus.Unresolved, result.Status);
        Assert.Single(result.Candidates);
        Assert.False(result.Validation.Revalidated);
    }

    [Fact]
    public async Task Required_revalidated_external_source_currentness_failure_blocks_exact()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, 123));
        external.EqualityEvidence.Enqueue(CreateEquality(123, comparePassed: true));
        external.EqualityEvidence.Enqueue(CreateEquality(
            123,
            comparePassed: true,
            sourceAvailable: ProofOutcome.Failed));
        var target = new FakeTargetPort(CreateInitialTarget(1234));
        target.Revalidated = CreateRevalidatedTarget(1234, referenceEqual: true, current: true, live: true);

        var result = await Resolve(external, target);

        Assert.Equal(CorrelationStatus.Unresolved, result.Status);
        Assert.Single(result.Candidates);
        Assert.False(result.Validation.Revalidated);
    }

    [Fact]
    public async Task Initial_compare_elements_false_keeps_candidate_but_short_circuits_revalidation()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, 123));
        external.EqualityEvidence.Enqueue(CreateEquality(123, comparePassed: false));
        var target = new FakeTargetPort(CreateInitialTarget(1234));
        target.Revalidated = CreateRevalidatedTarget(1234, referenceEqual: true, current: true, live: true);

        var result = await Resolve(external, target);

        Assert.Equal(CorrelationStatus.Unresolved, result.Status);
        Assert.Single(result.Candidates);
        Assert.Null(result.PrimaryCandidateId);
        Assert.Empty(result.Candidates[0].Relationships);
        Assert.Equal(0, target.RevalidationCalls);
        Assert.Single(external.EqualityEvidenceConsumed);
    }

    [Fact]
    public async Task Missing_hwnd_does_not_call_target()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, observedHwnd: null));
        var target = new FakeTargetPort(CreateInitialTarget(1234));

        var result = await Resolve(external, target);

        Assert.Equal(CorrelationStatus.Unresolved, result.Status);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, target.BeginCalls);
        Assert.Empty(external.EqualityEvidenceConsumed);
    }

    [Fact]
    public async Task Process_mismatch_short_circuits_before_external_equality()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, 123));
        var target = new FakeTargetPort(CreateInitialTarget(5678));

        var result = await Resolve(external, target);

        Assert.Equal(CorrelationStatus.Unresolved, result.Status);
        Assert.Single(result.Candidates);
        Assert.Null(result.PrimaryCandidateId);
        Assert.Empty(external.EqualityEvidenceConsumed);
    }

    [Fact]
    public async Task Missing_target_candidate_is_unresolved_without_target_revalidation()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, 123));
        external.EqualityEvidence.Enqueue(CreateEquality(123, comparePassed: true));
        var target = new FakeTargetPort(CreateInitialTarget(1234, candidate: null));

        var result = await Resolve(external, target);

        Assert.Equal(CorrelationStatus.Unresolved, result.Status);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, target.RevalidationCalls);
        Assert.Single(external.EqualityEvidenceConsumed);
    }

    [Fact]
    public async Task Target_revalidation_change_is_unresolved_without_external_revalidation()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, 123));
        external.EqualityEvidence.Enqueue(CreateEquality(123, comparePassed: true));
        external.EqualityEvidence.Enqueue(CreateEquality(123, comparePassed: true));
        var target = new FakeTargetPort(CreateInitialTarget(1234));
        target.Revalidated = CreateRevalidatedTarget(1234, referenceEqual: false, current: false, live: false);

        var result = await Resolve(external, target);

        Assert.Equal(CorrelationStatus.Unresolved, result.Status);
        Assert.Single(result.Candidates);
        Assert.False(result.Validation.Revalidated);
        Assert.Single(external.EqualityEvidenceConsumed);
    }

    [Fact]
    public async Task Revalidated_external_compare_failure_is_unresolved()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, 123));
        external.EqualityEvidence.Enqueue(CreateEquality(123, comparePassed: true));
        external.EqualityEvidence.Enqueue(CreateEquality(123, comparePassed: false));
        var target = new FakeTargetPort(CreateInitialTarget(1234));
        target.Revalidated = CreateRevalidatedTarget(1234, referenceEqual: true, current: true, live: true);

        var result = await Resolve(external, target);

        Assert.Equal(CorrelationStatus.Unresolved, result.Status);
        Assert.Single(result.Candidates);
        Assert.Null(result.PrimaryCandidateId);
        Assert.Equal(2, external.EqualityEvidenceConsumed.Count);
    }

    [Fact]
    public async Task Target_acquisition_failure_short_circuits_external_revalidation_and_reports_agent_effect()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, 123));
        external.EqualityEvidence.Enqueue(CreateEquality(123, comparePassed: true));
        var target = new FakeTargetPort(CreateInitialTarget(1234));
        var error = new OperationErrorDto(OperationErrorCode.StaleHandle, "The handle generation is stale.");
        var acquisitionFailure = CreateAcquisitionFailureTarget(1234, error);
        target.Revalidated = acquisitionFailure;

        var result = await Resolve(external, target);

        Assert.Equal(CorrelationStatus.Unresolved, result.Status);
        Assert.NotNull(result.OperationError);
        Assert.Equal(OperationErrorCode.StaleHandle, result.OperationError!.Code);
        Assert.Single(external.EqualityEvidenceConsumed);
        Assert.Equal(1, target.RevalidationCalls);
        Assert.Contains("NativeSpy.Agent.TryAcquire", result.Effects.Operations);
        Assert.DoesNotContain("WinForms.Control.FromHandle.Revalidate", result.Effects.Operations);
        Assert.DoesNotContain(
            acquisitionFailure.EvidenceFacts,
            fact => fact.Name is "ControlFromHandle" or "CurrentHwndMatches" or "ControlLive");
    }

    [Fact]
    public async Task External_operation_error_is_unresolved_and_preserved()
    {
        var external = new FakeExternalPort(CreateExternalEvidence(1234, 123));
        external.EqualityEvidence.Enqueue(new ExternalUiaEqualityEvidenceDto(
            new ExternalUiaCaptureRefDto("observation", "capture"),
            123,
            new[]
            {
                Fact("SourceAvailable", ProofOutcome.NotAvailable),
                Fact("ElementFromHandle", ProofOutcome.NotAvailable),
                Fact("CompareElements", ProofOutcome.NotAvailable)
            },
            Array.Empty<CorrelationLimitationDto>(),
            new OperationErrorDto(OperationErrorCode.TargetOperationFailed, "synthetic UIA failure")));
        var target = new FakeTargetPort(CreateInitialTarget(1234));

        var result = await Resolve(external, target);

        Assert.Equal(CorrelationStatus.Unresolved, result.Status);
        Assert.NotNull(result.OperationError);
        Assert.Equal(OperationErrorCode.TargetOperationFailed, result.OperationError!.Code);
        Assert.Single(result.Candidates);
    }

    private static Task<CorrelationResultDto> Resolve(
        FakeExternalPort external,
        FakeTargetPort target)
    {
        return new CorrelationCoordinator().ResolveUiaToWinFormsAsync(
            external,
            target,
            Policy,
            CancellationToken.None);
    }

    private static ExternalUiaEvidenceDto CreateExternalEvidence(int processId, ulong? observedHwnd)
    {
        return new ExternalUiaEvidenceDto(
            new ExternalUiaCaptureRefDto("observation", "capture"),
            processId,
            observedHwnd,
            new[]
            {
                Fact("SourceAvailable", ProofOutcome.Passed),
                Fact("CurrentHwndObserved", observedHwnd is null ? ProofOutcome.Failed : ProofOutcome.Passed),
                Fact("ExternalElementProcessIdentity", ProofOutcome.Passed)
            },
            Array.Empty<CorrelationLimitationDto>());
    }

    private static ExternalUiaEqualityEvidenceDto CreateEquality(
        ulong hwnd,
        bool comparePassed,
        ProofOutcome sourceAvailable = ProofOutcome.Passed)
    {
        return new ExternalUiaEqualityEvidenceDto(
            new ExternalUiaCaptureRefDto("observation", "capture"),
            hwnd,
            new[]
            {
                Fact("SourceAvailable", sourceAvailable),
                Fact("ElementFromHandle", ProofOutcome.Passed),
                Fact("CompareElements", comparePassed ? ProofOutcome.Passed : ProofOutcome.Failed)
            },
            Array.Empty<CorrelationLimitationDto>());
    }

    private static FrameworkCorrelationEvidenceDto CreateInitialTarget(int processId)
    {
        return CreateInitialTarget(processId, CreateTarget());
    }

    private static FrameworkCorrelationEvidenceDto CreateInitialTarget(
        int processId,
        CorrelationTargetRefDto? candidate,
        IEnumerable<CorrelationValidationFactDto>? diagnosticFacts = null)
    {
        var validationFacts = new[]
        {
            Validation("CurrentHwndMatches", candidate is null ? ValidationOutcome.Failed : ValidationOutcome.Passed),
            Validation("ControlLive", candidate is null ? ValidationOutcome.Failed : ValidationOutcome.Passed)
        };
        return new FrameworkCorrelationEvidenceDto(
            "winforms",
            processId,
            candidate,
            new[]
            {
                Fact("ControlFromHandle", candidate is null ? ProofOutcome.Failed : ProofOutcome.Passed)
            },
            validationFacts.Concat(diagnosticFacts ?? Array.Empty<CorrelationValidationFactDto>()),
            PassiveEffects(),
            Array.Empty<AdapterMetadataDto>(),
            Array.Empty<CorrelationLimitationDto>());
    }

    private static FrameworkCorrelationEvidenceDto CreateRevalidatedTarget(
        int processId,
        bool referenceEqual,
        bool current,
        bool live,
        IEnumerable<CorrelationValidationFactDto>? diagnosticFacts = null)
    {
        var validationFacts = new[]
        {
            Validation("CandidateResolved", ValidationOutcome.Passed),
            Validation("CurrentHwndMatches", current ? ValidationOutcome.Passed : ValidationOutcome.Changed),
            Validation("ControlLive", live ? ValidationOutcome.Passed : ValidationOutcome.Changed)
        };
        return new FrameworkCorrelationEvidenceDto(
            "winforms",
            processId,
            candidateTarget: null,
            new[]
            {
                Fact("CandidateResolved", ProofOutcome.Passed),
                Fact(
                    "ControlFromHandleReferenceEqual",
                    referenceEqual ? ProofOutcome.Passed : ProofOutcome.Failed),
                Fact("ControlFromHandle", current ? ProofOutcome.Passed : ProofOutcome.Failed)
            },
            validationFacts.Concat(diagnosticFacts ?? Array.Empty<CorrelationValidationFactDto>()),
            PassiveEffects(),
            Array.Empty<AdapterMetadataDto>(),
            Array.Empty<CorrelationLimitationDto>());
    }

    private static FrameworkCorrelationEvidenceDto CreateAcquisitionFailureTarget(
        int processId,
        OperationErrorDto operationError)
    {
        return new FrameworkCorrelationEvidenceDto(
            "winforms",
            processId,
            candidateTarget: null,
            new[]
            {
                Fact("CandidateResolved", ProofOutcome.NotAvailable),
                Fact("ControlFromHandleReferenceEqual", ProofOutcome.NotAvailable)
            },
            new[]
            {
                Validation("CandidateResolved", ValidationOutcome.Changed)
            },
            PassiveEffects(new[] { "NativeSpy.Agent.TryAcquire" }),
            Array.Empty<AdapterMetadataDto>(),
            Array.Empty<CorrelationLimitationDto>(),
            operationError);
    }

    private static CorrelationTargetRefDto CreateTarget()
    {
        var handle = new HandleRefDto("session", "button", 1, HandleKind.ClrObject, "target");
        var type = new TypeIdentityDto(
            "System.Windows.Forms.Button, System.Windows.Forms",
            "System.Windows.Forms.Button",
            "System.Windows.Forms",
            "target",
            isValueType: false,
            Array.Empty<TypeRefDto>(),
            Array.Empty<TypeRefDto>());
        return new CorrelationTargetRefDto(
            CorrelationTargetKind.ManagedObject,
            managed: new ManagedObjectRefDto(handle, type, "target"));
    }

    private static CorrelationEffectSummaryDto PassiveEffects(IEnumerable<string>? operations = null)
    {
        return new CorrelationEffectSummaryDto(
            new[] { EffectCategory.Passive },
            FrameworkStateEffect.None,
            ApplicationCallbackEffect.None,
            Array.Empty<CallbackDetailDto>(),
            VisibleMutationEffect.NotRequested,
            operations ?? Array.Empty<string>());
    }

    private static CorrelationEvidenceFactDto Fact(
        string name,
        ProofOutcome outcome)
    {
        return new CorrelationEvidenceFactDto(name, outcome, EvidenceKind.Deterministic);
    }

    private static CorrelationValidationFactDto Validation(
        string name,
        ValidationOutcome outcome)
    {
        return new CorrelationValidationFactDto(name, outcome);
    }

    private sealed class FakeExternalPort : IExternalUiaObservationPort
    {
        public FakeExternalPort(ExternalUiaEvidenceDto evidence)
        {
            Evidence = evidence;
        }

        public ExternalUiaEvidenceDto Evidence { get; }

        public Queue<ExternalUiaEqualityEvidenceDto> EqualityEvidence { get; } = new();

        public List<ExternalUiaEqualityEvidenceDto> EqualityEvidenceConsumed { get; } = new();

        public Task<ExternalUiaEvidenceDto> CaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Evidence);
        }

        public Task<ExternalUiaEqualityEvidenceDto> CompareWithHwndAsync(
            ExternalUiaCaptureRefDto capture,
            ulong hwnd,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = EqualityEvidence.Dequeue();
            EqualityEvidenceConsumed.Add(evidence);
            return Task.FromResult(evidence);
        }
    }

    private sealed class FakeTargetPort : IWinFormsCorrelationPort
    {
        public FakeTargetPort(FrameworkCorrelationEvidenceDto initial)
        {
            Initial = initial;
        }

        public FrameworkCorrelationEvidenceDto Initial { get; }

        public FrameworkCorrelationEvidenceDto? Revalidated { get; set; }

        public int BeginCalls { get; private set; }

        public int RevalidationCalls { get; private set; }

        public Task<FrameworkCorrelationEvidenceDto> BeginCurrentHwndAsync(
            ulong hwnd,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeginCalls++;
            return Task.FromResult(Initial);
        }

        public Task<FrameworkCorrelationEvidenceDto> RevalidateCurrentHwndAsync(
            ulong hwnd,
            HandleRefDto candidateHandle,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RevalidationCalls++;
            return Task.FromResult(
                Revalidated ?? throw new InvalidOperationException("The fake target has no revalidation response."));
        }
    }
}
