# Iteration 2 Implementation Notes

## Scope

I2 replaces the I1 test-only CLR object/type identity path with a production framework-neutral Agent identity/session core:

```text
real WinForms Control
        ↓
NativeSpy.Agent ClrAgentSession
        ↓ weak HandleRef + production TypeIdentity
WinForms evidence adapter
        ↓
NativeSpy.Client exact correlation evaluator
```

The I1 UIA3/FlaUI and Client certainty semantics remain unchanged.

## Projects and dependency structure

```text
NativeSpy.Protocol (netstandard2.0)
       ↑                 ↑
NativeSpy.Agent          NativeSpy.Client
       ↑                 ↑
NativeSpy.Agent.WinForms NativeSpy.FlaUI
       ↑                 ↑
NativeSpy.TestTarget     NativeSpy.IntegrationTests
```

`NativeSpy.Agent` targets `net10.0` and references only `NativeSpy.Protocol`. It has no WinForms, WPF, FlaUI, Client, transport, or test dependency. `NativeSpy.Agent.WinForms` references Agent and Protocol.

## Public Agent seam

`ClrAgentSession` is already active at construction and implements:

```csharp
IManagedObjectReferenceService
```

The seam has only:

```csharp
Register(object)
TryAcquire(HandleRefDto)
```

Registration returns a complete `ManagedObjectRefDto`, including production TypeIdentity and BoundaryId. Acquisition returns an operation-local `ManagedObjectAcquisition : IDisposable`. Expected lifecycle outcomes are structured `OperationErrorDto` results; API misuse and broken invariants remain exceptions.

`ManagedObjectAcquisition` retains only the target and immutable handle. Disposal is idempotent and clears the strong target. It retains no session, registry, CWT, or HandleRecord.

## Session and handle semantics

A session has an immutable opaque `SessionId` and states:

```text
Active → Closing → Closed
```

`Close()` and `Dispose()` are idempotent. The session gate defines registration/acquisition/close linearization. Existing acquisitions remain usable after close; new operations return `SessionClosed`.

Handle IDs and generations are positive/session-local and never reused. They use implementation formats equivalent to:

```text
HandleId:   clr-object-<counter>
Generation: <positive counter>
```

Generation is validated by exact equality only. It has no arithmetic, ordering, HWND, or WinForms-currentness meaning.

Validation outcomes are deterministic:

```text
wrong session/unknown ID → InvalidHandle
wrong kind               → InvalidHandle
wrong generation/boundary→ StaleHandle
collected target         → ObjectCollected
closed current session   → SessionClosed
unavailable registration ALC → RuntimeUnavailable
```

## Weak object registry

The object reverse index is:

```text
ConditionalWeakTable<object, ObjectIdentityEntry>
```

The handle map contains records with:

```text
immutable HandleRefDto
WeakReference<object>
Live | Collected
```

CWT values and handle records contain no strong target reference. The live record transitions once from `Live` to `Collected` when weak acquisition fails. Historical tombstones remain until session close, preventing rebinding and preserving exact `ObjectCollected` diagnosis without rooting the application graph.

## Runtime boundaries and TypeIds

Runtime boundaries are keyed by actual:

```csharp
AssemblyLoadContext.GetLoadContext(type.Assembly)
```

The boundary registry uses a weak-keyed CWT and detached entries only. A null load context returns `RuntimeUnavailable`; it is not silently mapped to Default. Default ALC and custom ALCs receive distinct session-local opaque BoundaryIds.

Type identity is cached with:

```text
ConditionalWeakTable<Type, TypeIdentityEntry>
```

Entries retain detached TypeIdentity data only. TypeIds use session-local opaque identifiers equivalent to `clr-type-<counter>`. I2 populates the existing useful metadata—name, assembly information, optional MVID, value-type flag, and BoundaryId—without recursive type graphs, member discovery, or reflection APIs.

## WinForms integration

`WinFormsCurrentHwndAdapter` now receives only `IManagedObjectReferenceService`. The three I1 delegates and all adapter-owned type/handle identity code were removed.

Begin registration uses the complete Agent-produced `ManagedObjectRefDto`.

Revalidation is bounded and preserves the deterministic proof:

```text
TryAcquire(handle)
    ↓ success
using acquisition
    ↓
Control.FromHandle(hwnd)
    ↓
ReferenceEquals(acquisition.Target, currentControl)
```

If Agent acquisition fails, the adapter does not perform another `Control.FromHandle` operation. It emits only truthful bounded acquisition-failure evidence and propagates the exact Agent error code. Client proof names, normalization, certainty, and `SameManagedElement` ownership remain unchanged.

## Test target and bridge

The synthetic target creates exactly one `ClrAgentSession` and passes the same identity service to:

- `WinFormsCurrentHwndAdapter`;
- the test bridge.

`TestManagedHandleTable` was removed. The former text-inspection probe acquired through the production Agent, retained the target only for the bounded UI-thread operation, and returned a private test-wire error code on failure. That JSON Lines bridge was test-only and is not production transport.

## Verification tests

`NativeSpy.Agent.Tests` covers:

- same-reference deduplication;
- `Equals` versus reference identity;
- weak non-retention and forced-GC collection;
- no handle rebinding;
- session isolation;
- forged handle fields;
- lifecycle and idempotent close/dispose;
- TypeId and boundary behavior;
- equivalent types in distinct collectible ALCs;
- concurrent registration/acquisition;
- concurrent distinct-target registration with stable TypeId and BoundaryId behavior;
- acquisition/close and registration/close races;
- result factory invariants.

The focused WinForms adapter tests additionally prove truthful registration failure evidence and acquisition-failure short-circuiting. The integration test additionally proves:

```text
same live Button → identical SessionId/HandleId/Generation/Kind/BoundaryId
UIA ObservationId → stable
UIA CaptureId → fresh per attempt
forged generation → StaleHandle
valid handle → actual Button.Text
```

## Deliberate deferrals

I2 does not implement:

- explicit leases;
- generic reflection/member inspection;
- ObjectSpy;
- production Protocol.Json or transport;
- Named Pipes, Host, handshake, authentication, or attach/bootstrap;
- execution-context/dispatcher infrastructure;
- WPF, UIA2, ProviderAware, .NET Framework, x86, ARM, or cross-AppDomain support;
- complete collectible-ALC unload invalidation or boundary enumeration.

I3 is production protocol/Host/Named Pipes. I4 is minimal generic CLR inspection and ObjectSpy Lite.
