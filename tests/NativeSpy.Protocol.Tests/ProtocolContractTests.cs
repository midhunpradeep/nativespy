using System.Reflection;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;
using Xunit;

namespace NativeSpy.Protocol.Tests;

public sealed class ProtocolContractTests
{
    [Fact]
    public void Managed_object_reference_requires_a_ClrObject_handle()
    {
        var itemHandle = new HandleRefDto("session", "item", 1, HandleKind.Item);

        Assert.Throws<ArgumentException>(() => new ManagedObjectRefDto(itemHandle));
    }

    [Fact]
    public void Managed_object_reference_requires_consistent_runtime_boundaries()
    {
        var handle = CreateHandle();
        var type = new TypeIdentityDto(
            "type",
            "Example.Type",
            "Example",
            "other-boundary",
            isValueType: false,
            Array.Empty<TypeRefDto>(),
            Array.Empty<TypeRefDto>());

        Assert.Throws<ArgumentException>(() => new ManagedObjectRefDto(handle, type));
        Assert.Throws<ArgumentException>(() => new ManagedObjectRefDto(handle, boundaryId: "other-boundary"));
        Assert.Throws<ArgumentException>(() => new ManagedObjectRefDto(
            new HandleRefDto("session", "handle", 1, HandleKind.ClrObject),
            type));

        var valid = new ManagedObjectRefDto(
            handle,
            new TypeIdentityDto(
                "type",
                "Example.Type",
                "Example",
                "boundary",
                isValueType: false,
                Array.Empty<TypeRefDto>(),
                Array.Empty<TypeRefDto>()),
            boundaryId: "boundary");
        Assert.Equal("boundary", valid.BoundaryId);
    }

    [Fact]
    public void Object_lifecycle_error_codes_are_defined()
    {
        Assert.Equal(
            OperationErrorCode.ObjectCollected,
            new OperationErrorDto(OperationErrorCode.ObjectCollected).Code);
        Assert.Equal(
            OperationErrorCode.RuntimeUnavailable,
            new OperationErrorDto(OperationErrorCode.RuntimeUnavailable).Code);
    }

    [Fact]
    public void Target_envelope_requires_exactly_one_matching_payload()
    {
        var managed = new ManagedObjectRefDto(CreateHandle());
        var framework = new FrameworkEntityRefDto("wpf", FrameworkEntityKind.PresentationRoot);

        Assert.Throws<ArgumentException>(() =>
            new CorrelationTargetRefDto(CorrelationTargetKind.ManagedObject));
        Assert.Throws<ArgumentException>(() =>
            new CorrelationTargetRefDto(CorrelationTargetKind.ManagedObject, managed, framework));
        Assert.Throws<ArgumentException>(() =>
            new CorrelationTargetRefDto(CorrelationTargetKind.NativeEntity, managed: managed));

        var valid = new CorrelationTargetRefDto(CorrelationTargetKind.ManagedObject, managed: managed);
        Assert.Same(managed, valid.Managed);
        Assert.Null(valid.Framework);
        Assert.Null(valid.Native);
    }

    [Fact]
    public void Source_envelope_requires_exactly_one_matching_payload()
    {
        var observation = new ExternalObservationRefDto("observation", "capture");
        var target = new CorrelationTargetRefDto(
            CorrelationTargetKind.ManagedObject,
            managed: new ManagedObjectRefDto(CreateHandle()));

        Assert.Throws<ArgumentException>(() =>
            new CorrelationSourceDto(CorrelationSourceKind.ExternalObservation));
        Assert.Throws<ArgumentException>(() =>
            new CorrelationSourceDto(CorrelationSourceKind.ExternalObservation, observation, target));
        Assert.Throws<ArgumentException>(() =>
            new CorrelationSourceDto(CorrelationSourceKind.NativeTarget, externalObservation: observation));

        var valid = new CorrelationSourceDto(CorrelationSourceKind.NativeTarget, nativeTarget: target);
        Assert.Same(target, valid.NativeTarget);
        Assert.Null(valid.ExternalObservation);
    }

    [Fact]
    public void Handle_generation_is_positive_and_HWND_zero_is_not_an_observation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HandleRefDto("session", "handle", 0, HandleKind.ClrObject));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HwndInfoDto(0, "hwnd-1"));

        var generation = new GenerationRefDto("winforms", "hwnd", "7", "control-1");
        Assert.Equal("7", generation.Value);
        Assert.Equal("control-1", generation.ScopeId);
    }

    [Fact]
    public void Type_identity_uses_non_null_defensive_relationship_collections()
    {
        var genericArguments = new List<TypeRefDto> { new("T", "boundary") };
        var interfaces = new List<TypeRefDto> { new("I", "boundary") };
        var identity = new TypeIdentityDto(
            "T1",
            "Example.Type",
            "Example",
            "boundary",
            isValueType: false,
            genericArguments,
            interfaces);

        genericArguments.Clear();
        interfaces.Clear();

        Assert.Single(identity.GenericArguments);
        Assert.Single(identity.Interfaces);
        Assert.Throws<ArgumentNullException>(() =>
            new TypeIdentityDto("T1", "Example.Type", "Example", "boundary", false, null!, Array.Empty<TypeRefDto>()));
    }

    [Fact]
    public void Type_reference_is_detached_and_requires_both_identifiers()
    {
        Assert.Throws<ArgumentException>(() => new TypeRefDto(" ", "boundary"));
        Assert.Throws<ArgumentException>(() => new TypeRefDto("T1", " "));

        var typeRef = new TypeRefDto("T1", "boundary");
        Assert.Equal("T1", typeRef.TypeId);
        Assert.Equal("boundary", typeRef.BoundaryId);
    }

    [Fact]
    public void Detached_metadata_values_have_one_matching_payload()
    {
        var nullValue = DetachedMetadataValueDto.Null();
        var integer = DetachedMetadataValueDto.Integer(4);
        var values = new List<DetachedMetadataValueDto> { integer };
        var array = DetachedMetadataValueDto.Array(values);
        values.Clear();

        Assert.Equal(DetachedMetadataValueKind.Null, nullValue.Kind);
        Assert.Null(nullValue.IntegerValue);
        Assert.Equal(4, integer.IntegerValue);
        Assert.Single(array.ArrayValue!);
        Assert.Throws<ArgumentNullException>(() => DetachedMetadataValueDto.String(null!));
    }

    [Fact]
    public void Detached_metadata_object_keys_are_unique_and_ordinal()
    {
        var duplicate = new[]
        {
            new DetachedMetadataPropertyDto("key", DetachedMetadataValueDto.Integer(1)),
            new DetachedMetadataPropertyDto("key", DetachedMetadataValueDto.Integer(2))
        };
        var caseDistinct = new[]
        {
            new DetachedMetadataPropertyDto("key", DetachedMetadataValueDto.Integer(1)),
            new DetachedMetadataPropertyDto("Key", DetachedMetadataValueDto.Integer(2))
        };

        Assert.Throws<ArgumentException>(() => DetachedMetadataValueDto.Object(duplicate));
        Assert.Equal(2, DetachedMetadataValueDto.Object(caseDistinct).ObjectValue!.Count);
    }

    [Fact]
    public void Public_evidence_summaries_may_repeat_labels_until_normalization()
    {
        var proofStep = new ProofStepDto("Repeated", ProofOutcome.Passed, EvidenceKind.Structural);
        var validationCheck = new ValidationCheckDto("Repeated", ValidationOutcome.Passed);
        var summary = new CorrelationProofSummaryDto(
            CorrelationProofMethod.BoundedStructuralEvidence,
            new[] { proofStep, proofStep },
            new[] { validationCheck, validationCheck },
            Array.Empty<CorrelationLimitationDto>(),
            revalidated: true);
        var validation = new CorrelationValidationDto(
            new[] { validationCheck, validationCheck },
            Array.Empty<GenerationRefDto>(),
            revalidated: true);

        Assert.Equal(2, summary.Steps.Count);
        Assert.Equal(2, summary.ValidationDetails.Count);
        Assert.Equal(2, validation.Checks.Count);
    }

    [Fact]
    public void Adapter_metadata_requires_a_positive_schema_version_and_detached_payload()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AdapterMetadataDto("adapter", "schema", 0, DetachedMetadataValueDto.Null()));
        Assert.Throws<ArgumentNullException>(() =>
            new AdapterMetadataDto("adapter", "schema", 1, null!));
    }

    [Fact]
    public void Policy_bounds_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationPolicyDto(CorrelationPolicyMode.Conservative, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationPolicyDto(CorrelationPolicyMode.Conservative, 1, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationPolicyDto(CorrelationPolicyMode.Conservative, 1, 1, 0));
    }

    [Fact]
    public void Undefined_closed_enum_values_are_rejected_at_protocol_boundaries()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HandleRefDto("session", "handle", 1, (HandleKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OperationErrorDto((OperationErrorCode)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationPolicyDto((CorrelationPolicyMode)999, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProofStepDto("proof", (ProofOutcome)999, EvidenceKind.Deterministic));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProofStepDto("proof", ProofOutcome.Passed, (EvidenceKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ValidationCheckDto("validation", (ValidationOutcome)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationEvidenceSummaryDto("evidence", (EvidenceKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationTargetRefDto((CorrelationTargetKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationSourceDto((CorrelationSourceKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FrameworkEntityRefDto("wpf", (FrameworkEntityKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NativeEntityRefDto((NativeBoundaryKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RelationshipClaimDto((RelationshipKind)999, CreateTarget(), RelationshipScope.CurrentObservation));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RelationshipClaimDto(RelationshipKind.SameManagedElement, CreateTarget(), (RelationshipScope)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationProofSummaryDto((CorrelationProofMethod)999, Array.Empty<ProofStepDto>(), Array.Empty<ValidationCheckDto>(), Array.Empty<CorrelationLimitationDto>(), true));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationEffectSummaryDto(new[] { (EffectCategory)999 }, FrameworkStateEffect.None, ApplicationCallbackEffect.None, Array.Empty<CallbackDetailDto>(), VisibleMutationEffect.NotRequested, Array.Empty<string>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationEffectSummaryDto(new[] { EffectCategory.Passive }, (FrameworkStateEffect)999, ApplicationCallbackEffect.None, Array.Empty<CallbackDetailDto>(), VisibleMutationEffect.NotRequested, Array.Empty<string>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationEffectSummaryDto(new[] { EffectCategory.Passive }, FrameworkStateEffect.None, (ApplicationCallbackEffect)999, Array.Empty<CallbackDetailDto>(), VisibleMutationEffect.NotRequested, Array.Empty<string>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationEffectSummaryDto(new[] { EffectCategory.Passive }, FrameworkStateEffect.None, ApplicationCallbackEffect.None, Array.Empty<CallbackDetailDto>(), (VisibleMutationEffect)999, Array.Empty<string>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationDirectionCapabilityDto((CorrelationDirection)999, Array.Empty<RelationshipKind>(), CorrelationCapabilityMode.Direct, CorrelationPolicyMode.Conservative, 1, false, false, Array.Empty<CorrelationLimitationDto>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationDirectionCapabilityDto(CorrelationDirection.UiaToNative, Array.Empty<RelationshipKind>(), (CorrelationCapabilityMode)999, CorrelationPolicyMode.Conservative, 1, false, false, Array.Empty<CorrelationLimitationDto>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationDirectionCapabilityDto(CorrelationDirection.UiaToNative, Array.Empty<RelationshipKind>(), CorrelationCapabilityMode.Direct, (CorrelationPolicyMode)999, 1, false, false, Array.Empty<CorrelationLimitationDto>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CorrelationDirectionCapabilityDto(CorrelationDirection.UiaToNative, new[] { (RelationshipKind)999 }, CorrelationCapabilityMode.Direct, CorrelationPolicyMode.Conservative, 1, false, false, Array.Empty<CorrelationLimitationDto>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateResult((CorrelationStatus)999, new[] { CreateCandidate("c1") }, null, policy: CreatePolicy()));
    }

    [Fact]
    public void Relationship_claim_has_no_status_or_confidence_field()
    {
        var propertyNames = typeof(RelationshipClaimDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Status", propertyNames);
        Assert.DoesNotContain("Confidence", propertyNames);
        Assert.DoesNotContain("IsExact", propertyNames);
    }

    [Fact]
    public void Result_selection_invariants_are_enforced()
    {
        var candidate = CreateCandidate("c1");
        var policy = CreatePolicy();

        var exact = CreateResult(CorrelationStatus.Exact, new[] { candidate }, "c1", policy: policy);
        Assert.Equal("c1", exact.PrimaryCandidateId);

        Assert.Throws<ArgumentException>(() =>
            CreateResult(CorrelationStatus.Exact, new[] { candidate }, null, policy: policy));
        Assert.Throws<ArgumentException>(() =>
            CreateResult(CorrelationStatus.Ambiguous, new[] { candidate }, "c1", policy: policy));
        Assert.Throws<ArgumentException>(() =>
            CreateResult(CorrelationStatus.Exact, new[] { candidate }, "other", policy: policy));

        var ambiguous = CreateResult(CorrelationStatus.Ambiguous, new[] { candidate }, null, policy: policy);
        Assert.Null(ambiguous.PrimaryCandidateId);
        Assert.Equal(CorrelationSourceKind.ExternalObservation, ambiguous.Source.SourceKind);
    }

    [Fact]
    public void Operation_error_requires_an_unresolved_result_but_candidates_may_remain()
    {
        var candidate = CreateCandidate("c1");
        var error = new OperationErrorDto(OperationErrorCode.TargetTimeout, "timed out");
        var unresolved = CreateResult(
            CorrelationStatus.Unresolved,
            new[] { candidate },
            null,
            policy: CreatePolicy(),
            operationError: error);

        Assert.Same(error, unresolved.OperationError);
        Assert.Single(unresolved.Candidates);

        Assert.Throws<ArgumentException>(() =>
            CreateResult(
                CorrelationStatus.Exact,
                new[] { candidate },
                "c1",
                policy: CreatePolicy(),
                operationError: error));
    }

    [Fact]
    public void ProviderAware_hint_has_consistent_policy_invariants()
    {
        var candidate = CreateCandidate("c1");
        var conservative = CreatePolicy(CorrelationPolicyMode.Conservative);
        var providerAware = CreatePolicy(CorrelationPolicyMode.ProviderAware);

        var hinted = CreateResult(
            CorrelationStatus.Unresolved,
            new[] { candidate },
            null,
            policy: conservative,
            deeperProofAvailable: true,
            requiredPolicy: CorrelationPolicyMode.ProviderAware);
        Assert.Equal(CorrelationPolicyMode.ProviderAware, hinted.RequiredPolicy);

        Assert.Throws<ArgumentException>(() =>
            CreateResult(CorrelationStatus.Unresolved, new[] { candidate }, null, policy: conservative, requiredPolicy: CorrelationPolicyMode.Conservative));
        Assert.Throws<ArgumentException>(() =>
            CreateResult(CorrelationStatus.Unresolved, new[] { candidate }, null, policy: conservative, requiredPolicy: CorrelationPolicyMode.ProviderAware));
        Assert.Throws<ArgumentException>(() =>
            CreateResult(CorrelationStatus.Unresolved, new[] { candidate }, null, policy: providerAware, deeperProofAvailable: true, requiredPolicy: CorrelationPolicyMode.ProviderAware));
    }

    [Fact]
    public void Protocol_assembly_has_no_framework_or_UIA_references()
    {
        var forbidden = typeof(HandleRefDto).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name ?? string.Empty)
            .Where(static name =>
                name.StartsWith("FlaUI.", StringComparison.OrdinalIgnoreCase)
                || name.Equals("PresentationFramework", StringComparison.OrdinalIgnoreCase)
                || name.Equals("WindowsBase", StringComparison.OrdinalIgnoreCase)
                || name.Equals("System.Windows.Forms", StringComparison.OrdinalIgnoreCase)
                || name.Equals("UIAutomationClient", StringComparison.OrdinalIgnoreCase)
                || name.Equals("UIAutomationTypes", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(forbidden);
    }

    private static HandleRefDto CreateHandle()
    {
        return new HandleRefDto("session", "handle", 1, HandleKind.ClrObject, "boundary");
    }

    private static CorrelationTargetRefDto CreateTarget()
    {
        return new CorrelationTargetRefDto(
            CorrelationTargetKind.ManagedObject,
            managed: new ManagedObjectRefDto(CreateHandle()));
    }

    private static CorrelationCandidateDto CreateCandidate(string id)
    {
        return new CorrelationCandidateDto(
            id,
            CreateTarget(),
            Array.Empty<RelationshipClaimDto>(),
            Array.Empty<CorrelationEvidenceSummaryDto>(),
            proofSummary: null,
            CreateValidation(),
            CreateEffects(),
            Array.Empty<CorrelationLimitationDto>());
    }

    private static CorrelationValidationDto CreateValidation(bool revalidated = true)
    {
        return new CorrelationValidationDto(Array.Empty<ValidationCheckDto>(), Array.Empty<GenerationRefDto>(), revalidated);
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

    private static CorrelationPolicyDto CreatePolicy(CorrelationPolicyMode mode = CorrelationPolicyMode.Conservative)
    {
        return new CorrelationPolicyDto(mode, 8, 32, 32);
    }

    private static CorrelationSourceDto CreateSource()
    {
        return new CorrelationSourceDto(
            CorrelationSourceKind.ExternalObservation,
            externalObservation: new ExternalObservationRefDto("observation", "capture"));
    }

    private static CorrelationResultDto CreateResult(
        CorrelationStatus status,
        IEnumerable<CorrelationCandidateDto> candidates,
        string? primaryCandidateId,
        CorrelationPolicyDto policy,
        bool deeperProofAvailable = false,
        CorrelationPolicyMode? requiredPolicy = null,
        OperationErrorDto? operationError = null)
    {
        return new CorrelationResultDto(
            status,
            CorrelationDirection.UiaToNative,
            policy,
            CreateSource(),
            candidates,
            primaryCandidateId,
            proofSummary: null,
            CreateEffects(),
            CreateValidation(),
            Array.Empty<CorrelationConflictDto>(),
            Array.Empty<CorrelationLimitationDto>(),
            deeperProofAvailable,
            requiredPolicy,
            operationError);
    }
}
