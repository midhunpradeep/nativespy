# NativeSpy Project State

## Current iteration

**I3 — authenticated Named Pipes Host/Client over preserved UIA3/FlaUI to WinForms correlation**

## Implemented

- repository/bootstrap structure and SDK pin;
- framework-neutral correlation DTOs and enums;
- detached CLR handle and type identity contracts;
- strengthened managed-reference runtime-boundary invariants;
- Client-owned normalized proof and validation requirements;
- pure shared `CorrelationEvaluator`;
- asynchronous Client ports and coordinator for UiaToWinForms current-HWND correlation;
- UIA3 FlaUI session/source with stable observation identity, fresh capture IDs, numeric HWND, and stable adapter equality facts;
- framework-neutral `NativeSpy.Agent` with `ClrAgentSession` and session lifecycle;
- weak-by-default object identity using a weak reverse index and weak handle records;
- immutable session-local HandleIds, positive Generations, and collected-object tombstones;
- operation-local strong `ManagedObjectAcquisition` with deterministic lifecycle results;
- actual `AssemblyLoadContext`-based session-local runtime boundaries;
- weak Type identity cache and production session-local TypeIds;
- synchronous WinForms adapter composed with `IManagedObjectReferenceService`, including truthful registration and acquisition failure evidence;
- x64 synthetic WinForms target hosting one production Agent Host and one CLR session;
- strict bounded JSON wire codec with handshake, version negotiation, capabilities, limits, duplicate-property rejection, and separate protocol/operation error branches;
- same-account Named Pipes transport with explicit SID ACLs, pooled 4-byte big-endian framing, serialized writes, and process-instance identity helpers;
- single-client Host lifecycle and authentication using nonce plus exact PID/creation identity;
- monotonic Host-owned request budgets, bounded eight-request concurrency, one UI callback, and terminal session ownership;
- Named Pipe Client session with negotiated limits, canonical increasing request IDs, pending response correlation, and TargetExited/SessionClosed mapping;
- bootstrap-only stdout target and production integration port using Named Pipes;
- repeated-correlation assertion proving stable CLR handle identity and fresh UIA CaptureIds;
- Protocol, JSON codec, transport, Host/Client session, Agent lifetime/concurrency, and live integration tests;
- deterministic MSBuild artifact handoff from the target project's actual output directory.

## Not implemented / deferred

- attach/injection/bootstrap discovery beyond the explicit stdout descriptor and process lifetime integration beyond the single session;
- explicit leases, renewal, quotas, expiry schedulers, and reconnect semantics;
- generic reflection/member inspection, ObjectSpy, writes, invocation, and value conversion;
- WPF, UIA2, ProviderAware, and broader provider selection;
- .NET Framework, x86, ARM, cross-AppDomain support;
- complete collectible-ALC unload invalidation and boundary enumeration;
- generic execution-context/dispatcher infrastructure outside the fixed WinForms dispatcher.

## Next iterations

- **I3 — production Protocol/Host/Named Pipes transport and session handshake.**
- **I4 — minimal generic CLR inspection and ObjectSpy Lite.**

## Build status

- SDK: .NET 10.0.401
- TFMs: Protocol `netstandard2.0`; Agent/Agent.Tests `net10.0`; Client `net10.0`; UI/target/integration projects `net10.0-windows`;
- Platform: x64 Windows for the live UI path;
- Build: passing with 0 warnings and 0 errors;
- Tests: 112 passing, 0 skipped in the verified environment (28 Protocol, 15 Agent, 64 Client, 2 Agent.WinForms, 3 integration).

## Known limitations

- Handles are weak by default and may return `ObjectCollected` between operations; explicit leases are intentionally deferred.
- Tombstone metadata is retained for the lifetime of an active session and released on close; bounded pruning/compaction is deferred until real ObjectSpy workloads establish useful limits.
- The former JSON Lines bridge and test-only text command were removed; stdout carries only the bootstrap descriptor and RPC uses Named Pipes.
- Full collectible-ALC unload invalidation is not implemented; I2 only establishes actual ALC identity and keeps boundary caches weak.
- FlaUI and WinForms live objects remain isolated to their respective projects; Client sees detached facts only.
- The live integration test requires Windows, x64, modern .NET WinForms, and an interactive desktop/UIA environment.

## Architecture invariants

1. **Correlation is not identity.** HWND, RuntimeId, provider wrappers, and semantic coordinates are observations; CLR identity is an opaque session-scoped handle.
2. **Reference identity is authoritative.** Equal-but-distinct objects receive different handles; collected handles are never rebound.
3. **Weak between operations.** The registry does not root application objects; only an active acquisition retains a target strongly.
4. **Session ownership is strict.** Handles and TypeIds belong to exactly one session and are rejected by another session.
5. **Boundary identity is runtime-based.** Handle and TypeIdentity BoundaryIds come from actual `AssemblyLoadContext` identity.
6. **Client owns certainty.** Agent and WinForms emit evidence; Client owns normalization, requirements, certainty, and `SameManagedElement` claims.

I3 intentionally does not add attach/injection, reflection, generic inspection, ObjectSpy, reconnect, WPF, leases, or multi-client sessions.
