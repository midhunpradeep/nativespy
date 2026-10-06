# NativeSpy Project State

## Current iteration

**I4a — compact ObjectSpy Lite over authenticated UIA3/FlaUI to WinForms correlation**

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
- same-account Named Pipes transport with eager explicit SID ACL binding, pooled 4-byte big-endian framing, serialized writes, bounded frame/depth parsing, and process-instance identity helpers;
- single-client Host lifecycle and authentication using nonce plus exact PID/creation identity, fixed protocol operation declarations, and canonical/reserved-name validation;
- monotonic Host-owned request budgets, bounded eight-request concurrency, one UI callback, logical response commitment before physical delivery, terminal session ownership, and non-blocking shutdown observation of late work;
- Named Pipe Client session with negotiated fixed limits, canonical increasing request IDs, bounded pending/abandoned request state, late-response slot release, and TargetExited/SessionClosed mapping;
- bootstrap-only stdout target and production integration port using Named Pipes;
- repeated-correlation assertion proving stable CLR handle identity and fresh UIA CaptureIds;
- Protocol, JSON codec, transport, Host/Client session, authentication/request adversarial, late WinForms shutdown, Agent lifetime/concurrency, and live integration tests;
- deterministic MSBuild artifact handoff from the target project's actual output directory;
- four bounded CLR inspection operations with strict JSON union validation, opaque member IDs, authenticated continuation tokens, weak references, and hard quotas;
- explicit WinForms execution-context resolution with direct/adapter/unavailable/conflict outcomes and truthful Host timeout/late-work behavior;
- dedicated serialized MTA UIA3 worker, physical point preview/freeze observations, PID-only candidate validation, explicit candidate/root HWND exclusion, and bounded hard-stuck quarantine;
- `NativeSpy.ObjectSpy` Exact-only coordinator with transactional freeze candidates, separate preview/committed UIA facts, quarantine-preserving CLR state, independent CLR navigation epochs, lazy property reads, member paging, bounded field batches, explicit object-reference navigation, late-result rejection, and overlay port;
- runnable WPF-only client `NativeSpy.ObjectSpy.App` with SourceInitialized main-window registration, overlay HWND lifecycle registration, a nonactivating click-through overlay, asynchronous bounded shutdown, and controlled synthetic WinForms target bootstrap;
- semantic protocol/value validation including required CLR wire-collection presence, nested malformed CLR-wire rejection, malformed-success client/session policy, no-total `clr.listMembers` wire-shape proof, Agent value/lifetime/member-shape and collectible-ALC proof, malformed-request session survival, deterministic preview/freeze/candidate race and quarantine tests, FlaUI normal-failure versus hard-timeout coverage, live point-preview/self-exclusion proof, real HWND-less WPF-child UIA-ancestry/exclusion proof, and executable shutdown proof;

## Not implemented / deferred

- attach/injection/bootstrap discovery beyond the explicit stdout descriptor and process lifetime integration beyond the single session;
- explicit leases, renewal, expiry schedulers, and reconnect semantics;
- attach/injection/bootstrap discovery beyond the controlled I4a target bootstrap;
- writes, invocation, collection browsing/enumeration, recursive inspection, and full Object Browser behavior;
- WPF target support, UIA2, ProviderAware, and broader provider selection;
- .NET Framework, x86, ARM, cross-AppDomain support;
- complete collectible-ALC unload invalidation and boundary enumeration;
- generic execution-context/dispatcher infrastructure outside the fixed WinForms dispatcher.

## Next iterations

- **Future** — attach/bootstrap and broader inspection/provider capabilities remain deferred; do not infer those features from I4a.

## Build status

- SDK: .NET 10.0.401
- TFMs: Protocol `netstandard2.0`; Agent/Agent.Tests `net10.0`; Client `net10.0`; UI/target/integration projects `net10.0-windows`;
- Platform: x64 Windows for the live UI path;
- Build: passing with 0 warnings and 0 errors;
- Tests: 263 passing, 0 skipped in the verified environment (95 Protocol, 22 Agent, 64 Client, 2 Agent.WinForms, 2 FlaUI, 78 integration).

## Known limitations

- Handles are weak by default and may return `ObjectCollected` between operations; explicit leases are intentionally deferred.
- Tombstone metadata is retained for the lifetime of an active session and released on close; bounded pruning/compaction is deferred until real ObjectSpy workloads establish useful limits.
- The former JSON Lines bridge and test-only text command were removed; stdout carries only the bootstrap descriptor and RPC uses Named Pipes.
- Full collectible-ALC unload invalidation is not implemented; I2 only establishes actual ALC identity and keeps boundary caches weak.
- FlaUI and WinForms live objects remain isolated to their respective projects; Client sees detached facts only.
- The live point-preview, self-exclusion, HWND-less WPF-child ancestry, and executable-shutdown integration tests require Windows, x64, modern .NET WinForms/WPF, and an interactive desktop/UIA environment.
- The coordinate contract is explicitly physical-pixel based and signed-coordinate tested; manual mixed-DPI hardware validation is unavailable in the current environment.
- I3 fixes JSON depth at 64 and does not negotiate or expose a per-session depth override.
- Late target work remains counted during an active session, but terminal Host shutdown does not wait indefinitely for it. Queued WinForms work that never starts settles as unavailable; already-running target work is never forcibly aborted.

## Architecture invariants

1. **Correlation is not identity.** HWND, RuntimeId, provider wrappers, and semantic coordinates are observations; CLR identity is an opaque session-scoped handle.
2. **Reference identity is authoritative.** Equal-but-distinct objects receive different handles; collected handles are never rebound.
3. **Weak between operations.** The registry does not root application objects; only an active acquisition retains a target strongly.
4. **Session ownership is strict.** Handles and TypeIds belong to exactly one session and are rejected by another session.
5. **Boundary identity is runtime-based.** Handle and TypeIdentity BoundaryIds come from actual `AssemblyLoadContext` identity.
6. **Client owns certainty.** Agent and WinForms emit evidence; Client owns normalization, requirements, certainty, and `SameManagedElement` claims.
7. **Exact gates CLR identity.** Non-Exact correlation retains evidence but never authorizes CLR inspection.
8. **Inspection is explicit and bounded.** Metadata does not execute getters; property reads are user-requested; values and member pages obey fixed quotas.
9. **UIA ownership is isolated.** Live UIA objects stay in FlaUI's serialized MTA session; CLR objects stay Agent-owned; ObjectSpy receives detached facts and transport-neutral ports.

I4a intentionally does not add attach/injection, writes, invocation, collection enumeration, recursive inspection, reconnect, WPF target support, UIA2, leases, or multi-client sessions.
