# Iteration 0 Implementation Notes

## What was implemented

Iteration 0 establishes the NativeSpy repository and the framework-neutral certainty foundation needed by later WPF, WinForms, and FlaUI walking slices.

`NativeSpy.Protocol` contains detached contracts for handles, type references, HWND observations, operation errors, correlation targets/sources, relationships, generation observations, adapter metadata, proof, validation, effects, capabilities, candidates, policy, and results.

`NativeSpy.Client` contains the normalized evaluator boundary and one pure `CorrelationEvaluator`. It does not discover candidates, call providers, use UIA, perform revalidation, or construct complete coordinator results.

## Repository/project structure

```text
NativeSpy.sln
global.json
Directory.Build.props

src/
  NativeSpy.Protocol/       netstandard2.0
  NativeSpy.Client/         net10.0

tests/
  NativeSpy.Protocol.Tests/ net10.0
  NativeSpy.Client.Tests/   net10.0

docs/
  ITERATION-0-IMPLEMENTATION-NOTES.md
```

The parent `research/` corpus was not copied or modified. The implementation repository is the child `nativespy/` directory.

## Dependency graph

```text
NativeSpy.Protocol
       ↑
NativeSpy.Client

NativeSpy.Protocol.Tests ──→ NativeSpy.Protocol
NativeSpy.Client.Tests   ──→ NativeSpy.Client ──→ NativeSpy.Protocol
```

No framework/UIA assembly is referenced by `NativeSpy.Protocol`.

## Important contract types

- `CorrelationTargetRefDto` is an explicit tagged envelope for exactly one managed, framework, or native payload.
- `RelationshipClaimDto` expresses semantic relationships independently from certainty status.
- `CorrelationCandidateDto` is request-scoped candidate bookkeeping plus target, relations, evidence, validation, effects, and limitations.
- `CorrelationProofSummaryDto` reports public evidence; it does not define certainty requirements.
- `CorrelationValidationDto` carries validation checks, generation observations, and the authoritative `Revalidated` flag.
- `CorrelationEffectSummaryDto` reports framework state, callbacks, visible mutation, categories, and operation names independently from status.
- `CorrelationResultDto` keeps canonical candidates, optional primary selection, conflicts, limitations, policy hints, and operation errors separate.
- `AdapterMetadataDto` carries a versioned codec-neutral `DetachedMetadataValueDto` tree.
- `GenerationRefDto` carries adapter-owned opaque generation observations; its value has no universal arithmetic meaning.
- `DetachedMetadataValueDto` supports only null, Boolean, integer, decimal, floating-point, string, array, and object values, with defensive copies and one matching payload.

## Certainty architecture

```text
raw/framework evidence
        ↓
Client normalization
    ProofSteps[]
    ValidationChecks[]
    ExactRequirements[]
    ValidationRequirements[]
    StrongEvidenceSatisfied
        ↓
CorrelationEvaluator
        ↓
status + optional primary candidate
```

The public proof summary is a reporting DTO. The internal `CorrelationCandidateAssessment` owns normalized proof and validation facts so the evaluator does not accidentally derive policy from a framework adapter's labels or from an optional reporting summary. `Candidate.Validation.Revalidated` remains the single lifecycle revalidation flag.

`ProofRequirement` requires an exact step name/evidence-kind match. Only a deterministic requirement may explicitly allow `NotAvailable` to qualify for `HighConfidence`. Missing facts are valid incomplete runtime evidence and resolve conservatively; duplicate normalized keys and invalid requirement configuration are rejected as programming errors. `Exact` additionally requires at least one deterministic Exact requirement.

## Evaluator control flow

1. An established positive semantic no-counterpart proof returns `NoDirectMapping`.
2. More than one candidate returns `Ambiguous`; no candidate is selected.
3. A required proof or validation conflict returns `Ambiguous`.
4. No candidates without positive semantic absence returns `Unresolved`.
5. One candidate must have all required validation checks passing and `Candidate.Validation.Revalidated == true`.
6. At least one deterministic Exact proof requirement is required for `Exact`, and every required proof passes.
7. Otherwise, `HighConfidence` requires strong normalized evidence, at least one permitted unavailable deterministic requirement, every other required proof passing, and current validation.
8. All other cases return `Unresolved`.

Effects, target kinds, relationships, metadata, policy hints, and non-required diagnostic facts never select a status. ProviderAware is never run automatically.

## Five most important invariants

1. **Correlation is not identity.** HWND, RuntimeId, provider wrappers, and semantic coordinates are observations; CLR identity is an opaque session-scoped handle.
2. **Status is not relationship.** `Exact + GridCell`, `Exact + PresentationRoot`, and `Unresolved + ToolStripItem` remain expressible independently.
3. **Zero candidates is not `NoDirectMapping`.** That status requires positive semantic absence proof.
4. **Multiple candidates never receive a heuristic winner.** Ambiguity has no primary candidate.
5. **HighConfidence requires specifically permitted unavailable deterministic proof, not failed proof.** Structural absence, failure, conflict, or not-attempted evidence cannot qualify.

## Representative tests

- A complete one-candidate proof/currentness path returns `Exact` and one primary.
- Two candidates remain `Ambiguous` even when proof appears complete, preventing heuristic selection.
- Required proof and validation conflicts are `Ambiguous`, while unrelated diagnostic conflicts do not invalidate required proof.
- One or more allowed unavailable deterministic steps plus strong/current evidence return `HighConfidence`; failed, not-attempted, structural, or unpermitted absence does not.
- Structural, descriptive, or geometry-only Exact requirements remain `Unresolved`; at least one deterministic Exact requirement is required.
- Representative undefined closed-enum values are rejected at Protocol DTO boundaries with `ArgumentOutOfRangeException`.
- Changed, unavailable, failed, or incomplete lifecycle validation returns `Unresolved`, and revalidation is read from `CorrelationValidationDto`.
- Positive semantic absence is the only route to `NoDirectMapping`; an empty candidate set alone is `Unresolved`.
- RuntimeId-like, descriptive, geometry, relationship, metadata, and effect facts cannot define Exact without normalized requirements.
- Target/source tagged envelopes reject mixed or mismatched payloads, managed references reject non-`ClrObject` handles, and results enforce primary/error/policy-hint invariants.
- The Protocol assembly-reference tripwire rejects future direct FlaUI, UIA, WPF, or WinForms dependencies.

## Deliberate deferrals

I0 contains no FlaUI, UIA3/COM, WPF, WinForms, IPC, attach/bootstrap, Agent, ObjectSpy, reflection, handle registry, or serialization codec. The iteration intentionally uses synthetic evidence and normalized assessments rather than production adapter interfaces. I1 will introduce actual FlaUI and WinForms seams alongside the first real Button path.

## Decisions made during implementation

- The implementation uses `CorrelationResultDto.Conflicts` rather than duplicating alternatives in an `AlternativesOrConflicts` property; `Candidates` is the canonical alternative collection.
- `TypeIdentityDto` uses bounded `TypeRefDto` links for related types rather than recursive full descriptors.
- Protocol identifiers are validated as non-empty and compared ordinally; arbitrary descriptive strings are not given a generic comparison policy.
- Handle generations use positive `long` tokens, while relation generations use opaque strings.
- `DetachedMetadataValueDto` uses typed factories backed by a private validating constructor.
- Standard .NET argument exceptions distinguish malformed local construction from runtime operation outcomes.

## Surprises/blockers

None. The .NET 10 SDK was installed before implementation. NuGet restore completed successfully.

## What the project owner should understand before I1

The evaluator is deliberately not a framework detector or candidate finder. I1 must supply detached FlaUI/target evidence and a Client normalizer that constructs requirements conservatively. The I0 status vocabulary and primary-selection rules should remain unchanged when the first WinForms current-HWND proof is added.
