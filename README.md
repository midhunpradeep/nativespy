# NativeSpy

NativeSpy is a Windows/.NET inspection system intended to bridge external UI Automation observations—such as FlaUI elements—to the actual managed and framework entities behind them.

## Architecture

The eventual flow is:

```text
FlaUI
  ↓
NativeSpy.Client
  ↓
NativeSpy.Protocol
  ↓
Agent
  ↓
real CLR/framework objects
```

NativeSpy keeps correlation separate from identity. Managed objects use opaque, session-scoped `HandleRefDto` values; HWNDs, RuntimeIds, provider wrappers, and framework metadata are observations or evidence, not durable identity.

## Current state

This repository contains **Iteration 1 — the WinForms/FlaUI walking skeleton**:

- `NativeSpy.Protocol` (`netstandard2.0`) — detached evidence, validation, policy, and operation-error DTOs;
- `NativeSpy.Client` (`net10.0`) — asynchronous evidence ports, current-HWND normalization, orchestration, and the unchanged pure certainty evaluator;
- `NativeSpy.FlaUI` (`net10.0-windows`) — UIA3 attachment, AutomationId lookup, fresh attempt-scoped capture references, HWND observation, and stable FlaUI equality facts;
- `NativeSpy.Agent.WinForms` (`net10.0-windows`) — synchronous target-side `Control.FromHandle`/`ReferenceEquals` evidence;
- `NativeSpy.TestTarget.WinForms` and `NativeSpy.IntegrationTests` — deterministic test-only WinForms process, bounded/fidelity-preserving JSON Lines bridge, session-local test handles/type IDs, and live end-to-end proof.

The supported I1 path is UIA3 `UiaToNative` correlation under explicit `Conservative` policy. Each resolution receives a fresh capture ID while retaining the source observation ID; Client normalization, not FlaUI, assigns initial/revalidation proof phases. It returns `Exact + SameManagedElement` only after initial and revalidated UIA equality, current-HWND WinForms lookup, and CLR reference equality. Transport, attach/bootstrap, handle lifetime/registry, WPF, UIA2, ProviderAware, and ObjectSpy remain deferred.

## Build and test

From this directory:

```text
dotnet restore
dotnet build
dotnet test
```

The selected SDK is recorded in `global.json`.

## Frozen certainty model

The shared evaluator returns exactly:

```text
Exact | HighConfidence | Ambiguous | NoDirectMapping | Unresolved
```

Evidence is normalized into Client-owned proof requirements before evaluation. `Conservative` and explicit `ProviderAware` policy hints never cause automatic escalation. Effects, relationships, target kinds, and metadata remain independent of certainty status.

See [`PROJECT_STATE.md`](PROJECT_STATE.md), [`CONTRIBUTING.md`](CONTRIBUTING.md), [`docs/ITERATION-0-IMPLEMENTATION-NOTES.md`](docs/ITERATION-0-IMPLEMENTATION-NOTES.md), and [`docs/ITERATION-1-IMPLEMENTATION-NOTES.md`](docs/ITERATION-1-IMPLEMENTATION-NOTES.md) for project status, contribution/Git conventions, and design notes.
