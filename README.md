# NativeSpy

NativeSpy is a Windows/.NET inspection system intended to bridge external UI Automation observations—such as FlaUI elements—to the actual managed and framework entities behind them.

## Architecture

The current path is:

```text
FlaUI
  ↓
NativeSpy.Client
  ↓ detached correlation evidence
NativeSpy.Protocol.Json
  ↓
NativeSpy.Client.NamedPipes ⇄ NamedPipe transport ⇄ NativeSpy.Agent.Host
                                                     ↓
                                         NativeSpy.Agent.Host.WinForms
                                                     ↓
                                        NativeSpy.Agent / ClrAgentSession
                                                     ↓
                                           real CLR/framework objects
```

NativeSpy keeps correlation separate from identity. Managed objects use opaque, session-scoped `HandleRefDto` values; HWNDs, RuntimeIds, provider wrappers, and framework metadata are observations or evidence, not durable identity.

## Current state

This repository contains **Iteration 4a — compact ObjectSpy Lite over the authenticated I0–I3 WinForms/FlaUI correlation slice**:

- `NativeSpy.Protocol` (`netstandard2.0`) — detached evidence, validation, policy, handle, type identity, operation-error, and shared operation-name contracts;
- `NativeSpy.Protocol.Json` (`netstandard2.0`) — strict bounded camelCase JSON wire codec with fixed depth 64, versioned envelopes, generic payloads, handshake, capabilities, limits, and separate protocol/operation errors;
- `NativeSpy.Agent` (`net10.0`) — framework-neutral `ClrAgentSession`, weak object identity registry, operation-local acquisitions, runtime-boundary identity, and production session-local TypeIds;
- `NativeSpy.Client` (`net10.0`) — asynchronous evidence ports, current-HWND normalization, orchestration, and the unchanged pure certainty evaluator;
- `NativeSpy.FlaUI` (`net10.0-windows`) — UIA3 attachment, dedicated serialized MTA ownership, point preview/freeze observations, fresh capture references, HWND observation, timeout quarantine, and stable FlaUI equality facts;
- `NativeSpy.Agent.WinForms` (`net10.0-windows`) — synchronous target-side `Control.FromHandle`/`ReferenceEquals` evidence composed with the production Agent identity service;
- `NativeSpy.Transport.NamedPipes` (`net10.0-windows`) — eager same-account ACL binding, pooled bounded framing, serialized writes, and process-instance identity helpers;
- `NativeSpy.Agent.Host` (`net10.0-windows`) — single-client authenticated Host lifecycle, fixed operation registry, monotonic request budgets, bounded concurrency, response commitment, and handler dispatch;
- `NativeSpy.Agent.Host.WinForms` (`net10.0-windows`) — the only Host-to-WinForms composition layer and asynchronous UI-thread dispatcher;
- `NativeSpy.Client.NamedPipes` (`net10.0-windows`) — one authenticated session per target with negotiated limits, request sequencing, pending response correlation, terminal lifecycle mapping, and transport-neutral CLR inspection ports;
- `NativeSpy.ObjectSpy` (`net10.0-windows`) — Exact-gated preview/freeze coordinator, detached UIA state, bounded CLR member paging/field reads, lazy property reads, and explicit object-reference navigation;
- `NativeSpy.ObjectSpy.App` (`net10.0-windows`) — runnable WPF ObjectSpy Lite client with physical-coordinate click-through overlay and controlled synthetic WinForms target bootstrap;
- `NativeSpy.TestTarget.WinForms` and `NativeSpy.IntegrationTests` — deterministic x64 target, bootstrap-only stdout, Named Pipes integration, I0–I3 regressions, and live ObjectSpy end-to-end proof.

The supported path remains UIA3 `UiaToNative` correlation under explicit `Conservative` policy. Each resolution receives a fresh capture ID while retaining the source observation ID; Client normalization, not FlaUI or Agent, assigns proof phases and certainty. It returns `Exact + SameManagedElement` only after initial and revalidated UIA equality, current-HWND WinForms lookup, and CLR reference equality. Only `CorrelationStatus.Exact` exposes CLR identity to ObjectSpy; all other statuses retain evidence and lock CLR inspection.

The I4a corrective pass keeps preview observations separate from committed frozen facts, commits a new freeze only after UIA acquisition succeeds, preserves the last healthy CLR graph when a later UIA operation is quarantined, and rejects late CLR navigation/property/page results by a separate navigation epoch. The CLR wire union now validates canonical integer ranges, exact floating-point bit widths, legal decimal flags, UTF-16 truncation boundaries, shallow value-type nesting, field counts, and payload size. Malformed CLR payloads are reported as non-terminal invalid requests; real handler failures remain terminal internal failures.

## Running the ObjectSpy client manually

From this directory on an interactive x64 Windows desktop:

```text
dotnet run --project src/NativeSpy.ObjectSpy.App/NativeSpy.ObjectSpy.App.csproj
```

The WPF client starts the controlled WinForms target, attaches UIA3, and displays `Connected to PID ...` when the authenticated session is ready. Activate the finder, move the physical cursor over the synthetic target's `NativeSpyTestButton`, and confirm that the click-through outline follows the candidate. Freeze the selection, confirm `Exact` correlation and the detached CLR member list, use `Read` for a property, use `Follow` on an object reference, and use `Back` to restore the previous object. Stop the finder or close the window to release the target, pipe session, UIA worker, and overlay.

## I2 identity model

A `ClrAgentSession` owns one session-local identity namespace. Registration uses reference identity (`ReferenceEquals` semantics), weakly indexes target objects, and returns an opaque `HandleRefDto` plus production `TypeIdentityDto`. Handle records retain only a `WeakReference<object>` and a lightweight tombstone. A collected object is never rebound; its exact historical handle returns `ObjectCollected`.

`TryAcquire` creates an operation-local `ManagedObjectAcquisition`. The acquisition is the only normal strong retention of the target and clears that reference when disposed. Session closure rejects new operations but does not revoke an acquisition that already obtained its target.

Runtime boundaries are based on actual `AssemblyLoadContext` identity. Boundary and TypeIds are opaque and session-local. Type and boundary caches use weak-keyed tables and do not retain target objects, Types, Assemblies, Modules, or ALCs strongly.

## I3 boundaries

The target publishes exactly one bootstrap descriptor on stdout; RPC uses only the authenticated Named Pipe. The session is single-client, non-reconnecting, same-account, and bound to the exact PID plus opaque process creation identity. Failed candidates do not consume the nonce, caller abandonment remains bounded by outstanding slots, and late target work cannot emit a second response. Terminal Host shutdown does not wait indefinitely for late target work; queued WinForms work that never starts becomes unavailable, while already-running target work is allowed to finish naturally. I3 deliberately excludes attach/injection, reflection, text inspection, WPF, leases, and ProviderAware behavior.

## Build and test

From this directory:

```text
dotnet restore
dotnet build
dotnet test
```

The selected SDK is recorded in `global.json`.

## Roadmap

- **I4a** — compact generic CLR inspection and ObjectSpy Lite (implemented).
- **I5+** — attach/bootstrap, broader browser behavior, and additional target/provider support (deferred).

See [`PROJECT_STATE.md`](PROJECT_STATE.md), [`CONTRIBUTING.md`](CONTRIBUTING.md), [`docs/ITERATION-0-IMPLEMENTATION-NOTES.md`](docs/ITERATION-0-IMPLEMENTATION-NOTES.md), [`docs/ITERATION-1-IMPLEMENTATION-NOTES.md`](docs/ITERATION-1-IMPLEMENTATION-NOTES.md), [`docs/ITERATION-2-IMPLEMENTATION-NOTES.md`](docs/ITERATION-2-IMPLEMENTATION-NOTES.md), and [`docs/ITERATION-4A-IMPLEMENTATION-NOTES.md`](docs/ITERATION-4A-IMPLEMENTATION-NOTES.md).
