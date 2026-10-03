# Iteration 1 Implementation Notes

## Scope

I1 implements only the confirmed UIA3 `UiaToNative` walking slice:

```text
FlaUI UIA3 AutomationElement
        ↓ detached external evidence
NativeSpy.Client coordinator/normalizer/evaluator
        ↓ detached target evidence
WinForms target Control.FromHandle
        ↓ temporary HandleRefDto resolution + ReferenceEquals
live System.Windows.Forms.Button
```

The supported policy is explicit `CorrelationPolicyDto(Conservative, ...)`. The coordinator rejects other policy modes rather than silently changing policy. `ExternalUiaEvidenceDto.ProcessId` is required and positive; zero is not a sentinel. Each bounded detached operation result has at most one optional `OperationErrorDto`.

I1 deliberately excludes production transport, attach/bootstrap, handle registry/lifetime, WPF, UIA2, ProviderAware behavior, reflection/member inspection, and ObjectSpy.

## Projects and dependency structure

```text
NativeSpy.Protocol (netstandard2.0)
   ↑                  ↑
NativeSpy.Client      NativeSpy.Agent.WinForms (net10.0-windows)
   ↑
NativeSpy.FlaUI (net10.0-windows)
   ↑
NativeSpy.IntegrationTests (net10.0-windows)

NativeSpy.TestTarget.WinForms (net10.0-windows)
   ├── NativeSpy.Agent.WinForms
   └── NativeSpy.Protocol

NativeSpy.Protocol.Tests ──→ NativeSpy.Protocol
NativeSpy.Client.Tests   ──→ NativeSpy.Client ──→ NativeSpy.Protocol
```

`NativeSpy.Agent.WinForms` references Protocol only. FlaUI owns the live `AutomationBase`/`AutomationElement`; the target project owns live WinForms controls. Client ports exchange detached DTOs.

## Evidence and proof

`FlaUiExternalSource` retains one live UIA3 source internally and reports:

- positive attached process identity;
- current numeric HWND from `NativeWindowHandle`;
- `FromHandle` acquisition for the initial and revalidation comparisons;
- `Compare` equality facts;
- detached descriptive UIA facts and limitations.

`WinFormsCurrentHwndAdapter` is synchronous and intended for target UI-thread dispatch. It reports:

- numeric-HWND `Control.FromHandle` lookup;
- current-HWND and live-control validation;
- a temporary `ClrObject` `HandleRefDto` for the candidate;
- `Control.FromHandle` re-resolution of the supplied handle;
- CLR `ReferenceEquals` against the current control;
- detached `System.Windows.Forms.Button` type identity and operation effects.

The Client coordinator performs capture, process identity gating, initial external equality, target candidate acquisition, target revalidation, and external equality revalidation. `WinFormsCurrentHwndNormalizer` constructs the evaluator requirements. `CorrelationEvaluator` remains the sole certainty authority. Failed or changed proof never becomes `Exact`; a mapped candidate is retained diagnostically with no primary when proof/currentness fails.

## Test-only integration bridge

`NativeSpy.TestTarget.WinForms` is a deterministic x64 target with a button named `NativeSpyTestButton` and text `NativeSpy Test Button`. `NativeSpy.IntegrationTests` launches it through `dotnet` from an MSBuild-copied artifact directory.

The private stdin/stdout JSON Lines bridge is not production transport. It has request IDs, one response per valid request, bounded request/response line checks, stderr-only diagnostics, UI-thread dispatch through `Control.Invoke`, graceful shutdown, and process-kill fallback. The strong object map is target-test-only and is accessed through supplied issue/resolve delegates.

The live test finds the UIA3 element by AutomationId and asserts:

- `CorrelationStatus.Exact`;
- one primary `ManagedObject` candidate with a `ClrObject` handle;
- `SameManagedElement` relationship;
- `System.Windows.Forms.Button` type identity;
- resolution through the temporary handle and `Button.Text == "NativeSpy Test Button"`.

## Verification

Verified from the `nativespy` directory:

```text
dotnet restore NativeSpy.sln     pass
dotnet build NativeSpy.sln       pass; 0 warnings, 0 errors
dotnet test NativeSpy.sln         pass; 82 total, 0 failed, 0 skipped
```

Test breakdown: 21 Protocol, 60 Client, and 1 live integration test. Roslyn CodeLens was reloaded against `NativeSpy.sln`; project dependencies showed the intended one-way graph, the coordinator call graph reached only the Client ports/normalizer/evaluator and Protocol DTOs, and project circular-dependency analysis reported zero cycles.

## Deviations and issues

No confirmed-scope deviations were found. The integration bridge is intentionally test-only and is the only process boundary added in I1. Its artifact-copy target is test infrastructure, not a production deployment mechanism. The live test remains environment-dependent on Windows/x64/interactive UIA, as intended.
