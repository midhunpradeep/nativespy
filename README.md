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

This repository contains **Iteration 0 only**:

- `NativeSpy.Protocol` (`netstandard2.0`) — framework-neutral correlation DTOs and contract invariants;
- `NativeSpy.Client` (`net10.0`) — normalized proof requirements and one pure certainty evaluator;
- Protocol and Client xUnit tests covering the decision table and architectural tripwires.

There is no FlaUI/UIA integration, WPF or WinForms adapter, agent, transport, attach layer, or object inspection implementation yet.

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

See [`PROJECT_STATE.md`](PROJECT_STATE.md) and [`docs/ITERATION-0-IMPLEMENTATION-NOTES.md`](docs/ITERATION-0-IMPLEMENTATION-NOTES.md) for the implementation status and design notes.
