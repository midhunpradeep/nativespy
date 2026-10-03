using NativeSpy.Client.Correlation;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.Client.Tests;

internal sealed class CorrelationScenarioBuilder
{
    private readonly List<string> _candidateIds = new();
    private readonly List<ProofStepDto> _proofSteps = new();
    private readonly List<ValidationCheckDto> _validationChecks = new();
    private readonly List<ProofRequirement> _exactRequirements = new();
    private readonly List<ValidationRequirement> _validationRequirements = new();
    private readonly List<RelationshipClaimDto> _relationships = new();
    private readonly List<EffectCategory> _effectCategories = new() { EffectCategory.Passive };
    private readonly List<string> _operations = new();
    private CorrelationPolicyMode _policyMode = CorrelationPolicyMode.Conservative;
    private CorrelationPolicyMode? _requiredPolicy;
    private bool _deeperProofAvailable;
    private bool _strongEvidenceSatisfied;
    private bool _revalidated;
    private PositiveNoDirectMappingAssessment? _positiveNoDirectMappingProof;
    private FrameworkStateEffect _frameworkState = FrameworkStateEffect.None;
    private ApplicationCallbackEffect _applicationCallbacks = ApplicationCallbackEffect.None;
    private VisibleMutationEffect _visibleMutation = VisibleMutationEffect.NotRequested;

    public CorrelationScenarioBuilder WithCandidate(string candidateId = "candidate-1")
    {
        _candidateIds.Add(candidateId);
        return this;
    }

    public CorrelationScenarioBuilder WithProofStep(
        string name,
        ProofOutcome outcome,
        EvidenceKind evidenceKind = EvidenceKind.Deterministic)
    {
        _proofSteps.Add(new ProofStepDto(name, outcome, evidenceKind));
        return this;
    }

    public CorrelationScenarioBuilder RequireExactStep(
        string name,
        EvidenceKind evidenceKind = EvidenceKind.Deterministic,
        bool unavailableForHighConfidenceAllowed = false)
    {
        _exactRequirements.Add(new ProofRequirement(name, evidenceKind, unavailableForHighConfidenceAllowed));
        return this;
    }

    public CorrelationScenarioBuilder AllowUnavailableForHighConfidence(
        string name,
        EvidenceKind evidenceKind = EvidenceKind.Deterministic)
    {
        return RequireExactStep(name, evidenceKind, unavailableForHighConfidenceAllowed: true);
    }

    public CorrelationScenarioBuilder RequireValidation(string name)
    {
        _validationRequirements.Add(new ValidationRequirement(name));
        return this;
    }

    public CorrelationScenarioBuilder WithValidationCheck(string name, ValidationOutcome outcome)
    {
        _validationChecks.Add(new ValidationCheckDto(name, outcome));
        return this;
    }

    public CorrelationScenarioBuilder WithStrongEvidence(bool satisfied = true)
    {
        _strongEvidenceSatisfied = satisfied;
        return this;
    }

    public CorrelationScenarioBuilder WithCurrentValidation()
    {
        _revalidated = true;
        return this;
    }

    public CorrelationScenarioBuilder WithChangedValidation(string name = "CurrentGeneration")
    {
        _validationChecks.Add(new ValidationCheckDto(name, ValidationOutcome.Changed));
        return this;
    }

    public CorrelationScenarioBuilder WithConflict(
        string name,
        bool validation = false,
        EvidenceKind evidenceKind = EvidenceKind.Deterministic)
    {
        if (validation)
        {
            _validationChecks.Add(new ValidationCheckDto(name, ValidationOutcome.Conflicted));
        }
        else
        {
            _proofSteps.Add(new ProofStepDto(name, ProofOutcome.Conflicted, evidenceKind));
        }

        return this;
    }

    public CorrelationScenarioBuilder WithPositiveNoDirectMappingProof(
        bool established = true,
        RelationshipKind? relationshipKind = null,
        RelationshipScope scope = RelationshipScope.CurrentObservation)
    {
        _positiveNoDirectMappingProof = new PositiveNoDirectMappingAssessment(established, relationshipKind, scope);
        return this;
    }

    public CorrelationScenarioBuilder WithProviderAwareHint()
    {
        _deeperProofAvailable = true;
        _requiredPolicy = CorrelationPolicyMode.ProviderAware;
        return this;
    }

    public CorrelationScenarioBuilder WithPolicy(CorrelationPolicyMode policyMode)
    {
        _policyMode = policyMode;
        return this;
    }

    public CorrelationScenarioBuilder WithEffects(
        EffectCategory category,
        FrameworkStateEffect frameworkState = FrameworkStateEffect.None,
        ApplicationCallbackEffect applicationCallbacks = ApplicationCallbackEffect.None,
        VisibleMutationEffect visibleMutation = VisibleMutationEffect.NotRequested,
        string? operation = null)
    {
        _effectCategories.Clear();
        _effectCategories.Add(category);
        _frameworkState = frameworkState;
        _applicationCallbacks = applicationCallbacks;
        _visibleMutation = visibleMutation;
        if (operation is not null)
        {
            _operations.Add(operation);
        }

        return this;
    }

    public CorrelationScenarioBuilder WithRelationship(RelationshipKind kind)
    {
        _relationships.Add(new RelationshipClaimDto(
            kind,
            CreateTarget(),
            RelationshipScope.CurrentObservation));
        return this;
    }

    public CorrelationEvaluationInput BuildInput()
    {
        var assessments = _candidateIds.Select(BuildAssessment).ToArray();
        return new CorrelationEvaluationInput(
            assessments,
            CreatePolicy(),
            _positiveNoDirectMappingProof,
            _deeperProofAvailable,
            _requiredPolicy);
    }

    private CorrelationCandidateAssessment BuildAssessment(string candidateId)
    {
        var candidate = new CorrelationCandidateDto(
            candidateId,
            CreateTarget(),
            _relationships,
            Array.Empty<CorrelationEvidenceSummaryDto>(),
            CreatePublicProofSummary(),
            new CorrelationValidationDto(_validationChecks, Array.Empty<GenerationRefDto>(), _revalidated),
            new CorrelationEffectSummaryDto(
                _effectCategories,
                _frameworkState,
                _applicationCallbacks,
                Array.Empty<CallbackDetailDto>(),
                _visibleMutation,
                _operations),
            Array.Empty<CorrelationLimitationDto>());

        return new CorrelationCandidateAssessment(
            candidate,
            _proofSteps,
            _validationChecks,
            _exactRequirements,
            _validationRequirements,
            _strongEvidenceSatisfied);
    }

    private CorrelationProofSummaryDto? CreatePublicProofSummary()
    {
        if (_proofSteps.Count == 0 && _validationChecks.Count == 0)
        {
            return null;
        }

        return new CorrelationProofSummaryDto(
            CorrelationProofMethod.BoundedStructuralEvidence,
            _proofSteps,
            _validationChecks,
            Array.Empty<CorrelationLimitationDto>(),
            _revalidated);
    }

    private CorrelationPolicyDto CreatePolicy()
    {
        return new CorrelationPolicyDto(_policyMode, 8, 32, 32);
    }

    private static CorrelationTargetRefDto CreateTarget()
    {
        return new CorrelationTargetRefDto(
            CorrelationTargetKind.ManagedObject,
            managed: new ManagedObjectRefDto(
                new HandleRefDto("session", "handle", 1, HandleKind.ClrObject, "boundary")));
    }
}
