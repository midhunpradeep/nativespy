# NativeSpy I5-A0 Attach Architecture

**Baseline:** `c02fbbceb7a389d806860bfe7d6e01d7f20cf7e9`
**Document status:** corrective architecture pass; frozen for I5-A1 implementation planning.
**Scope:** architecture documentation only. I5-A1 is not implemented by this document.

At review start, the repository `HEAD` was the stated baseline and production code, tests, project files, solution files, TFMs, and Git history were unchanged. This architecture document is the only working-tree artifact being created/updated by this pass.

Evidence labels:

- **REPOSITORY FACT** — directly observed in the baseline repository.
- **FROZEN OWNER DECISION** — fixed by the I5-A0 owner decisions in this request.
- **PUBLIC WINDOWS/.NET FACT** — documented platform/runtime behavior.
- **ARCHITECTURAL INFERENCE** — required design conclusion from the facts and frozen decisions.
- **FUTURE DECISION** — intentionally assigned to a later named iteration; A1 must not choose it.

Normative reading: repository observations in §2 are repository facts; the contract, identity, security, lifecycle, and ownership rules are frozen owner decisions unless explicitly marked otherwise; official API/runtime statements are public facts; implementation consequences are architectural inferences; later iteration assignments are future decisions.

---

## 1. Purpose and closure

I5-A0 defines the attach contract, lifecycle, security boundary, probe model, ownership rules, failure model, and exact I5-A1 implementation boundary.

The initial eventual end-to-end slice is:

```text
Windows x64
already-running target
.NET 10 CoreCLR
WinForms composition
same controller/target user SID
controller integrity not lower than target integrity
```

**FROZEN OWNER DECISION:** I5-A1 is not expected to attach a real arbitrary process. A1 implements semantic contracts, read-only probe behavior, fake/cooperative strategy seams, and deterministic lifecycle tests. B/C/E iterations provide the later real bootstrap, native entry, and arbitrary WinForms behavior.

The attach layer owns process-entry strategy and lifecycle. It does not own CLR object identity, protocol meaning, Agent registries, FlaUI objects, ObjectSpy selection state, or framework-specific target objects.

The existing authenticated I3 Named Pipe protocol remains the session transport and authentication boundary after attach-layer bootstrap handoff.

---

## 2. Existing repository facts and preserved invariants

### 2.1 Existing project facts

**REPOSITORY FACT:** the baseline contains these relevant projects:

```text
NativeSpy.Protocol              netstandard2.0
NativeSpy.Protocol.Json         netstandard2.0
NativeSpy.Agent                 net10.0
NativeSpy.Client                net10.0
NativeSpy.Transport.NamedPipes  net10.0-windows, x64
NativeSpy.Agent.Host            net10.0-windows, x64
NativeSpy.Agent.Host.WinForms   net10.0-windows, x64
NativeSpy.Client.NamedPipes     net10.0-windows, x64
NativeSpy.Agent.WinForms        net10.0-windows, x64
NativeSpy.FlaUI                 net10.0-windows, x64
NativeSpy.ObjectSpy             net10.0-windows, x64
NativeSpy.ObjectSpy.App         net10.0-windows, x64
```

The baseline project graph has no Roslyn-reported dependency cycle.

### 2.2 Existing process identity

**REPOSITORY FACT:** `ProcessIdentityDto` is a sealed class containing:

```text
ProcessId
ProcessStartIdentity
```

Its constructor validates positive PID and nonblank start identity, but it does not override object equality.

**FROZEN OWNER DECISION:** attach code must not rely on reference equality. The exact semantic equality is:

```text
left.ProcessId == right.ProcessId
AND
string.Equals(
    left.ProcessStartIdentity,
    right.ProcessStartIdentity,
    StringComparison.Ordinal)
```

All attach lifecycle maps, gates, dictionaries, and comparisons use an explicit attach-layer comparer implementing exactly that rule. Do not change `ProcessIdentityDto` merely to obtain equality.

**REPOSITORY FACT:** `ProcessIdentityReader` derives the process-start identity using process query access and process creation-time evidence.

### 2.3 Existing AgentHost and I3 behavior

**REPOSITORY FACT:** AgentHost currently:

- owns `BootstrapDescriptorWire`;
- creates its own Pipe name and bootstrap nonce through `AgentHostOptions.CreateDefault`;
- authenticates exact target identity;
- authenticates the bootstrap nonce;
- authenticates account SID;
- creates `ClrAgentSession` only after authentication;
- reports a fresh Agent `SessionId`;
- accepts one successful client;
- does not reconnect;
- stops on pipe loss, protocol failure, bootstrap expiry, or explicit stop;
- does not wait indefinitely for already-started target work.

The existing I3 sequence is:

```text
BootstrapDescriptorWire
    ↓
NamedPipeClientSession.ConnectAsync
    ↓
HelloRequestWire
    ↓
exact identity + nonce + protocol range + SID validation
    ↓
HelloResponseWire
    ↓
fresh Agent SessionId and capabilities
```

The descriptor remains internal to attach/session establishment. It is never an `AttachSession` public property.

### 2.4 Existing Agent identity invariants

**REPOSITORY FACT:** `ClrAgentSession` owns session-local:

- opaque HandleIds;
- exact positive handle generations;
- weak handle records;
- collected-object tombstones;
- TypeIds;
- runtime-boundary identity;
- operation-local strong acquisitions.

Handles, TypeIds, MemberRefs, continuation tokens, and framework evidence never cross an Agent session boundary.

### 2.5 Existing WinForms/ObjectSpy assumptions

**REPOSITORY FACT:** `WinFormsHostCompositionFactory` currently requires a caller-provided `Control`, and `WinFormsUiDispatcher` is anchored to that control. This is valid for the I4a controlled target but not for arbitrary attach.

**REPOSITORY FACT:** `ObjectSpyCoordinator` currently combines exact process identity, FlaUI, `IWinFormsCorrelationPort`, and `IClrInspectionPort`.

**REPOSITORY FACT:** the current FlaUI session ultimately attaches by PID. The final attach-integrated flow must correct this in I5-E1.

**REPOSITORY FACT:** the current frozen FlaUI selection does not deterministically release its live UIA source before the owning FlaUI session ends. This is an I5-E1 debt, not an A1 task.

---

## 3. I4a assumptions removed by I5

| I4a assumption | I5 replacement | Owner iteration |
|---|---|---|
| Target launches its own AgentHost | Attach strategy enters a resident NativeSpy bootstrap | B1/C1 |
| Target application constructs AgentHost options | Target-side NativeSpy bootstrap constructs Host options | B1 |
| Target startup publishes descriptor on stdout | One-use attach-layer rendezvous | B1 |
| Caller already knows a `Form`/`Control` | Requested composition plus later execution-context discovery | E0/E1 |
| ObjectSpy owns target lifetime | AttachManager owns NativeSpy lifecycle; target application survives detach | B1 |
| Tests know target artifact beside ObjectSpy | Later payload/artifact resolver | D0/D1 |
| Runtime is inferred from build configuration | Read-only runtime evidence and later payload policy | A1/D0 |
| Target architecture is fixed by project build | Windows architecture probe and strategy selection | A1/C1 |
| Process is selected by a launcher PID | Caller supplies exact `ProcessIdentityDto` | A1 |
| One target means one implicit Host | Resident process-global coordinator gate | B1/C1 |
| Failed client connection necessarily kills Host | Preserve current Host behavior; attach-owned cleanup explicitly requests stop when required | B0/B1 |

---

## 4. Project ownership and dependency graph

### 4.1 Required projects

#### `NativeSpy.Attach`

```text
TargetFramework: net10.0
platform-neutral managed project
no UseWindowsForms
no Windows-only P/Invoke
```

Owns:

- attach-domain contracts;
- identity comparer;
- AttachId allocation;
- probe/support semantic model;
- AttachManager;
- strategy interfaces;
- timeout/cancellation policy;
- failure/result unions;
- manager-owned lifecycle and session ownership;
- fake strategy/session seams.

#### `NativeSpy.Attach.Windows`

```text
TargetFramework: net10.0-windows
EnableWindowsTargeting: true
PlatformTarget: x64
```

Owns:

- Windows process probing;
- Windows primary-token SID/integrity evidence;
- later Windows rendezvous delivery;
- later peer-PID validation integration;
- later native process-entry mechanics;
- Windows-specific strategy policy.

It must not own WinForms semantics.

#### Test projects

```text
tests/NativeSpy.Attach.Tests
    net10.0

tests/NativeSpy.Attach.Windows.Tests
    net10.0-windows
    PlatformTarget: x64
```

These projects use repository-standard package/configuration conventions. Existing project TFMs are not changed.

### 4.2 Dependency graph

The final direction is:

```text
NativeSpy.Protocol
        ↑
NativeSpy.Client
        ↑
NativeSpy.Attach
        ↑
NativeSpy.Attach.Windows
```

More explicitly:

```text
NativeSpy.Attach
    → NativeSpy.Client
    → NativeSpy.Protocol

NativeSpy.Attach.Windows
    → NativeSpy.Attach
    → NativeSpy.Transport.NamedPipes       (when B1 rendezvous is implemented)

NativeSpy.Client.NamedPipes
    → NativeSpy.Client
    → NativeSpy.Protocol.Json
    → NativeSpy.Transport.NamedPipes
    → NativeSpy.Client connector seam

NativeSpy.ObjectSpy
    → NativeSpy.Attach
    → NativeSpy.Client
    → NativeSpy.FlaUI
    → NativeSpy.Protocol

NativeSpy.ObjectSpy.App
    → NativeSpy.ObjectSpy
    → NativeSpy.Attach.Windows
    → NativeSpy.Client.NamedPipes
```

`NativeSpy.Attach` must not reference `NativeSpy.Client.NamedPipes`. The outer composition wires the concrete Named Pipe connector into the AttachManager.

`NativeSpy.Client` must not reference `NativeSpy.Attach`.

`NativeSpy.Protocol`, `NativeSpy.Agent`, and `NativeSpy.Agent.Host` must not reference `NativeSpy.Attach`.

### 4.3 I3 connector placement

**FROZEN OWNER DECISION:** the transport-neutral authenticated client-session connector seam is declared in `NativeSpy.Client`.

Conceptual surface:

```text
IAuthenticatedClientSessionConnector
    ConnectAsync(
        AuthenticatedClientConnectRequest,
        CancellationToken)
        → AuthenticatedClientConnectResult
```

`AuthenticatedClientConnectRequest` contains exactly the bounded semantic handoff defined in §10.1:

```text
ExpectedTargetProcessIdentity
Endpoint
BootstrapAuthenticationSecret
SupportedProtocolRange
```

The connector result is semantic and supplies:

```text
Agent SessionId
IClrInspectionPort
capabilities
closed client adapter surfaces
local asynchronous disposal
```

`NativeSpy.Client.NamedPipes` implements the seam by adapting the existing `NamedPipeClientSession.ConnectAsync` and existing I3 handshake.

The attach-internal handoff may contain the validated descriptor needed for I3 connection, but neither the descriptor nor the transport object is exposed by `AttachSession`.

`NativeSpy.Attach` receives the connector through composition. A1 uses a fake connector. A1 must not redesign I3.

### 4.4 Adapter seam placement

The small client adapter contract remains in `NativeSpy.Client` beside the existing client correlation surfaces. This avoids a dependency cycle while preserving client-side WinForms ownership.

`AttachSession.TryGetAdapter(adapterId)` returns only an exact, closed, lifetime-bound adapter surface. It does not activate types, scan assemblies, or implement a plugin registry.

---

## 5. Exact process identity and AttachId

### 5.1 ProcessIdentity equality

The attach layer defines an explicit comparer:

```text
ProcessIdentityEquals(left, right):
    left is not null
    right is not null
    left.ProcessId == right.ProcessId
    AND
    string.Equals(
        left.ProcessStartIdentity,
        right.ProcessStartIdentity,
        StringComparison.Ordinal)
```

All of the following use this comparer:

- manager target maps;
- same-target lifecycle gates;
- probe identity checks;
- strategy state maps;
- rendezvous validation;
- detach/reconciliation generation checks;
- stale-result checks.

No dictionary uses the default `ProcessIdentityDto` reference comparer.

### 5.2 Probe identity consistency

A real Windows probe establishes one process incarnation around evidence collection:

```text
read exact ProcessIdentity
→ collect identity/runtime/security/architecture evidence
→ re-read exact ProcessIdentity
```

Rules:

| Observation | Result |
|---|---|
| No process exists before an identity is observed | `TargetNotFound` |
| Previously observed target disappears | `TargetExited` |
| PID exists but has another ProcessIdentity | `TargetIdentityChanged` |
| Any mismatch during collection | discard all collected evidence |
| Optional evidence unavailable but identity remains equal | `ProbeCompleted` with the affected dimension `Unknown` |
| Required identity/security access denied | `ProbeOutcome = AccessDenied` |

Evidence from two process incarnations is never combined.

### 5.3 AttachId ordering

For every valid explicit `AttachAsync` invocation:

```text
validate request contract
→ generate fresh AttachId
→ inspect manager lifecycle state
→ fail fast or acquire same-target operation
```

Therefore valid attempts returning any of these have their own fresh AttachId:

```text
AlreadyAttached
AttachInProgress
ProbeInconclusive
UnsupportedRuntime
UnsupportedArchitecture
UnsupportedComposition
AccessDenied
TargetExited
TargetIdentityChanged
```

Invalid programmer input is rejected before AttachId allocation:

```text
null request
invalid ProcessIdentity contract
invalid composition ID
```

A disposed manager is ordinary manager-state misuse, not a target-domain attach attempt; it may throw a state exception rather than create an AttachId.

AttachId is:

```text
opaque
fresh
non-reused
non-arithmetic
not an authentication credential
```

A failed AttachId is terminal metadata only. A successful AttachId remains the generation identity of its AttachSession.

### 5.4 Mandatory pre-entry identity revalidation

Every process-entry strategy must re-read the exact target `ProcessIdentityDto` immediately before its first invasive target side effect.

The required sequence is:

```text
fresh attach-owned probe completes
→ manager confirms eligibility
→ strategy prepares local resources only
→ strategy re-reads exact ProcessIdentity
→ compare using the frozen attach-layer value comparer
→ only an exact match permits the first invasive target operation
```

Before that final match, the strategy must not perform remote allocation, target-memory write, module load, thread creation, bootstrap invocation, or equivalent invasive work.

Results:

```text
PID no longer exists
    → TargetExited

PID now identifies another process incarnation
    → TargetIdentityChanged

required identity read denied
    → AccessDenied

other trustworthy identity validation unavailable
    → fail closed; no invasive action
```

The earlier probe identity is evidence, never authorization for later process entry.

---

## 6. Probe contract

### 6.1 Standalone versus attach-owned probe

`ProbeAsync` and the fresh probe inside `AttachAsync` are different operations.

Standalone `ProbeAsync` is:

```text
read-only
non-reserving
allowed while attach/detach is occurring
advisory about lifecycle state
```

Once a valid `AttachAsync` has allocated its AttachId, acquired the manager's same-target lifecycle operation, and entered `Probing`, a second `AttachAsync` for the same exact ProcessIdentity fails fast with:

```text
AttachInProgress
```

This same-target fail-fast rule applies during:

```text
Probing
Bootstrapping
Connecting
Detaching
Settling
CleanupUnknown
```

An `Attached` target returns:

```text
AlreadyAttached
```

Different exact ProcessIdentity values may proceed independently.

### 6.2 TargetProbeRequest

Semantic shape:

```text
TargetProbeRequest
    TargetProcessIdentity    required
    RequestedCompositionId   null or valid exact composition ID
```

Timeout policy is not duplicated in the request. The manager supplies `AttachTimeouts` configuration and uses the probe budget.

Null `RequestedCompositionId` means neutral probe.

### 6.3 Composition ID validation

Composition IDs are exact opaque identifiers with this A1 contract:

```text
non-null when supplied for AttachRequest
non-empty
no whitespace anywhere
ASCII lowercase identifier
maximum 64 characters
allowed characters: a-z, 0-9, '.', '_', '-'
comparison: StringComparison.Ordinal
```

Examples:

```text
valid:   winforms
invalid: WinForms
invalid: win forms
invalid: winforms<space>
invalid: win/forms
```

For `TargetProbeRequest`, null is valid and means neutral. Blank or otherwise invalid non-null values are programmer-contract errors, not probe failures.

Lexical validity and NativeSpy support are separate. The only initially recognized supported composition ID is exactly:

```text
winforms
```

using ordinal comparison. A syntactically valid but unsupported ID such as `wpf`, `foo`, or `future-composition` produces:

```text
CompositionSupport = KnownUnsupported
OverallAttachEligibility = NotEligible
```

An `AttachAsync` request with such an ID is a valid domain request and returns `UnsupportedComposition` before strategy invocation. It is not programmer misuse. A1 fake strategies may not expand the accepted composition set.

### 6.4 TargetProbeResult

The A1 public semantic model contains these dimensions:

```text
TargetProbeResult
    RequestedProcessIdentity
    ObservedProcessIdentity?
    ProbeOutcome
    TargetArchitecture
    ArchitectureSupport
    TargetRuntimeIdentity?
    RuntimeSupport
    RequestedCompositionId?
    CompositionSupport
    SecuritySupport
    OverallAttachEligibility
    ExistingNativeSpyState
    BoundedLimitations
    SafeDiagnostic?
```

There is deliberately no public `TargetLayoutSupport` dimension in A1.

Deployment model, single-file/self-contained heuristics, payload layout, and artifact compatibility belong to later strategy/payload policy and must not be invented by A1.

### 6.5 ProbeOutcome

```text
ProbeCompleted
TargetNotFound
TargetExited
TargetIdentityChanged
AccessDenied
InternalFailure
```

Caller cancellation is not a `ProbeOutcome`. It propagates as `OperationCanceledException`.

### 6.6 Support dispositions

Architecture, runtime, composition, and security support use:

```text
KnownSupported
KnownUnsupported
Unknown
```

Composition additionally has:

```text
NotRequested
```

Neutral probe semantics:

```text
CompositionSupport = NotRequested
OverallAttachEligibility = Unknown
```

A neutral probe never authorizes attach.

Composition-specific semantics:

```text
RequestedCompositionId = "winforms"
CompositionSupport = Unknown unless trustworthy permitted evidence exists
OverallAttachEligibility = Unknown when composition evidence is Unknown
```

For any syntactically valid composition ID other than `winforms` in A1:

```text
CompositionSupport = KnownUnsupported
OverallAttachEligibility = NotEligible
```

Unknown composition support is not attach-eligible.

A1 real Windows probing is not required to prove arbitrary-process WinForms availability. A1 fake probes may return `KnownSupported` and `Eligible` to exercise manager behavior.

### 6.7 Overall eligibility

A composition-specific attach may proceed only when all required A1 dimensions are known supported:

```text
ArchitectureSupport        = KnownSupported
RuntimeSupport             = KnownSupported
CompositionSupport         = KnownSupported
SecuritySupport            = KnownSupported
OverallAttachEligibility   = Eligible
```

A neutral probe always returns `Unknown` overall eligibility.

A1 does not add a public layout/payload dimension. Later strategy/payload checks may reject a target after A1's public probe has established stable facts.

### 6.8 Attach strategy guard

`AttachAsync` always invokes a fresh composition-specific probe. A caller cannot supply a previous `ProbeAsync` result as authorization.

After the fresh probe:

```text
OverallAttachEligibility != Eligible
    → IAttachStrategy MUST NOT be invoked

OverallAttachEligibility == Eligible
    → IAttachStrategy is invoked exactly once for that attach attempt
```

A1 tests must prove that Unknown and KnownUnsupported results never invoke the strategy.

### 6.9 Advisory NativeSpy state

`ExistingNativeSpyState` may report:

```text
NotObserved
Detached
Bootstrapping
Attached
Orphaned
Detaching
Settling
CleanupUnknown
Unknown
```

It is advisory. In particular:

```text
NotObserved != Detached
```

Authoritative lifecycle outcomes come from the manager's own operation or the resident coordinator during attach/lifecycle entry.

### 6.10 Probe access semantics

There is no ambiguous generic `AccessDisposition` field.

Required identity/security access failure is represented by:

```text
ProbeOutcome = AccessDenied
```

Optional evidence unavailable without definite access denial is represented by:

```text
ProbeOutcome = ProbeCompleted
relevant support dimension = Unknown
```

---

## 7. Runtime identity and support

### 7.1 Runtime identity

`TargetRuntimeIdentity` describes runtime evidence, not application targeting metadata:

```text
RuntimeFamily:
    CoreCLR
    FrameworkClr
    NativeOrNone
    Unknown

ProductVersion?
VersionEvidence:
    Exact
    MajorMinor
    MajorOnly
    Unknown
```

It must not claim a target TFM, deployment model, roll-forward policy, payload compatibility, or single-file/self-contained status unless a later strategy explicitly defines trustworthy evidence.

Runtime versions use a bounded structured value, not an arbitrary string:

```text
RuntimeVersion
    Major      nonnegative integer
    Minor?     nonnegative integer
    Build?     nonnegative integer
    Revision?  nonnegative integer
```

The same structured value is used for `TargetRuntimeIdentity.ProductVersion` and `AttachDiagnostic.RuntimeVersion` when applicable. No target-derived free-form version text crosses the public attach contract.

### 7.2 Architecture evidence and support

A1 uses documented Windows architecture observation based on `IsWow64Process2` and maps the effective process machine conservatively:

```text
X64
X86
Arm64
Other
Unknown
```

Initial architecture support is:

```text
X64     → KnownSupported
X86     → KnownUnsupported
Arm64   → KnownUnsupported
Other   → KnownUnsupported when positively identified
Unknown → Unknown
```

If architecture cannot be read because required process access is denied, return `ProbeOutcome = AccessDenied`. If the API/evidence is inconclusive without definite access denial, return `ArchitectureSupport = Unknown`.

Official API reference:

<https://learn.microsoft.com/en-us/windows/win32/api/wow64apiset/nf-wow64apiset-iswow64process2>

### 7.3 Initial runtime support

Initial runtime support is intended to be:

```text
RuntimeFamily = CoreCLR
runtime major = 10
```

However, A1 real Windows probing must not invent a compatibility promise from module filename or file version alone.

A1 real probing may observe bounded evidence such as:

```text
loaded coreclr.dll
loaded Framework CLR module
bounded version evidence
```

but module-name/version evidence alone must not produce `RuntimeSupport = KnownSupported` for the real attach path.

For A1 real probing:

```text
reliable Framework CLR evidence
    → RuntimeFamily = FrameworkClr
    → RuntimeSupport = KnownUnsupported

reliable native/no-managed-runtime evidence
    → RuntimeFamily = NativeOrNone
    → RuntimeSupport = KnownUnsupported

CoreCLR evidence that cannot yet prove the C0 attach contract
    → RuntimeFamily = CoreCLR
    → bounded observed version evidence
    → RuntimeSupport = Unknown

conflicting/inaccessible evidence
    → Unknown
```

A1 fake probes may return CoreCLR major 10 with `RuntimeSupport = KnownSupported` to exercise manager behavior. The first real evidence rule capable of declaring the native-entry slice `KnownSupported` is owned by C0.

### 7.4 Public .NET boundary

The .NET hosting documentation describes hosting/runtime-property APIs for a host interacting with a runtime. Those APIs do not justify inferring an arbitrary target application's TFM or deployment model from a coarse process label.

Official references:

- <https://learn.microsoft.com/dotnet/core/tutorials/netcore-hosting>
- <https://learn.microsoft.com/dotnet/core/native-interop/hosting>

A1 reports conservative evidence and leaves broader payload compatibility to D0.

---

## 8. Windows security identity and integrity policy

### 8.1 Controller identity

The controller user SID is derived from the controller process primary token.

Do not derive authorization from:

```text
thread impersonation token
caller-supplied SID
environment variables
user-name strings
```

The controller integrity level is also process-token evidence, not an arbitrary caller claim.

The controller-created rendezvous endpoint ACL is constructed from the controller's actual token/user identity. The target never supplies the identity used to secure the controller endpoint.

### 8.2 Target identity

The target user SID is derived from the target process primary token using documented Windows process/token APIs.

The controller never supplies the SID that AgentHost should trust.

Target-side AgentHost startup derives:

```text
AllowedUserSid
```

from the actual target process identity/token. The target never accepts a controller-supplied SID as its authorization policy.

### 8.3 Initial supported policy

Initial policy requires:

```text
target primary user SID == controller primary user SID
AND
target integrity level <= controller effective process integrity level
```

If SID matches but target integrity is higher, the target is not eligible. No automatic elevation is attempted.

SID equality does not prove that later process-entry rights are sufficient. Actual Windows API access checks remain authoritative for each later stage.

A1 may establish probe/security policy support only. It must not claim native-process-entry rights before C0 defines and tests them.

### 8.4 SecuritySupport

`SecuritySupport` uses:

```text
KnownSupported
KnownUnsupported
Unknown
```

Examples:

```text
same SID + permitted integrity       → KnownSupported
SID mismatch                         → KnownUnsupported
higher target integrity              → KnownUnsupported
required token evidence inaccessible → ProbeOutcome.AccessDenied
optional security evidence missing  → Unknown
```

---

## 9. Public attach contracts

The following are semantic contracts. Private helper/class names may be selected later without reopening these meanings.

### 9.1 AttachRequest

```text
AttachRequest
    TargetProcessIdentity    required
    RequestedCompositionId   required valid exact ID
```

Timeouts are manager configuration, not request fields.

The request contains no raw handles, remote addresses, HWNDs, target paths, command lines, payload paths, tokens, or framework objects.

A syntactically valid but unsupported composition is still a valid domain request. It returns `UnsupportedComposition` before strategy invocation and after AttachId allocation.

### 9.2 AttachResult

`AttachResult` is a strict result union:

```text
Success:
    AttachId
    AttachSession

Failure:
    AttachId
    AttachFailure
```

No valid result can contain both session and failure or neither.

A valid AttachAsync failure still reports the fresh AttachId allocated after request validation.

### 9.3 AttachFailure

`AttachFailure` is the failure branch of `AttachResult` and contains:

```text
AttachId
TargetProcessIdentity
AttachStage
AttachFailureCode
AttachDiagnostic?
PostFailureLifecycle?
```

The failure branch never contains an AttachSession. A caller-contract error is not represented as AttachFailure; it throws before AttachId allocation.

### 9.4 AttachSession

A successful session exposes semantic values only:

```text
AttachId
TargetProcessIdentity
TargetRuntimeIdentity
RequestedCompositionId
AgentSessionId
EffectiveCapabilities
IClrInspectionPort
TryGetAdapter(adapterId)
read-only lifecycle/terminal projection
```

It does not expose:

```text
BootstrapDescriptor
pipe name
bootstrap nonce
rendezvous endpoint
rendezvous token
LifecycleOwnerProof
NamedPipeClientSession
native handles
remote addresses
```

### 9.5 AttachSession ownership

An AttachSession is privately bound to the exact AttachManager instance that created it.

A foreign manager using another manager's session is programmer/state misuse and throws an ordinary .NET contract/state exception. It is not a target-domain `DetachFailed` result.

### 9.6 AttachSession.DisposeAsync

`DisposeAsync` is local cleanup only and is idempotent.

It may close local authenticated client resources and may indirectly cause pipe-loss cleanup, but it never claims authoritative target detach and never releases the target gate by assumption.

### 9.7 Adapter contract and lookup

`NativeSpy.Client` declares the deliberately small client-side adapter contract:

```text
IAttachAdapter
    AdapterId
```

There is no arbitrary metadata dictionary, plugin framework, dynamic activation, reflection scan, or assembly discovery.

`AttachSession` exposes conceptually:

```text
bool TryGetAdapter(
    string adapterId,
    out IAttachAdapter? adapter)
```

Adapter IDs use the same bounded lexical form as composition IDs:

```text
lowercase ASCII
1–64 characters
[a-z0-9._-]
StringComparison.Ordinal
```

The initial required WinForms adapter ID is:

```text
winforms.correlation
```

This is deliberately distinct from the composition ID `winforms`.

The WinForms adapter implements:

```text
IAttachAdapter
IWinFormsCorrelationPort
```

A successful `winforms` AttachSession requires both:

```text
IClrInspectionPort
adapter "winforms.correlation"
```

If I3 succeeds but either is unavailable:

```text
CapabilityMismatch
→ no AttachSession publication
→ tracked cleanup
```

After AttachSession becomes operationally terminal:

```text
TryGetAdapter(...) → false
```

Previously obtained adapter references remain bound to the terminated authenticated client session and reject further operations using existing session-closed semantics. No adapter migrates to a later AttachId.

`IClrInspectionPort` calls through a terminal AttachSession cannot begin and fail using existing session-closed semantics. Already-started Agent work follows existing I3 behavior and may finish naturally.

AttachSession operational surfaces are valid only while the local session is Active. After `DetachClaimed`, `LocallyDisposed`, `TargetExited`, or a terminal client event, no new CLR or adapter operation may begin.

### 9.8 DetachResult

`DetachResult` has an explicit authority distinction.

Authoritative lifecycle outcomes are:

```text
Detached
TargetExited
CleanupUnknown
```

`DetachFailed` is a `DetachResult` operation outcome, not an `AttachFailureCode` and not an authoritative terminal lifecycle state. Its lifecycle state must be `Settling` or `CleanupUnknown`.

Semantics:

```text
Detached
    Host/session closure positively verified; target remains running.

TargetExited
    Exact target exit positively verified; this is terminal but not
    successful application-preserving detach.

CleanupUnknown
    Positive closure is not established after shared cleanup/reconciliation
    policy; new attach remains blocked.

DetachFailed
    The coordinator/strategy reported a specific detach failure while the
    lifecycle remains Settling or CleanupUnknown; it is not a caller wait timeout.
```

If the per-caller `DetachCallerWait` budget expires while the shared operation is still known to be in progress, the caller receives a non-authoritative wait-timeout observation:

```text
Outcome = Settling
Authority = CallerWaitOnly
Failure = DetachFailed at stage Detach with caller-wait-timeout diagnostic
```

That observation is never cached as the target lifecycle result. The shared operation continues. Caller cancellation instead throws `OperationCanceledException` and produces no result.

Every result carries exact AttachId and ProcessIdentity. No valid result combines contradictory success/failure fields.

### 9.9 IAttachManager

Conceptual surface:

```text
ProbeAsync(TargetProbeRequest, CancellationToken)
    → TargetProbeResult

AttachAsync(AttachRequest, CancellationToken)
    → AttachResult

DetachAsync(AttachSession, CancellationToken)
    → DetachResult
```

The manager owns:

- AttachId allocation;
- exact identity comparer and maps;
- same-target lifecycle operation;
- fresh attach-owned probe;
- strategy invocation;
- connector invocation;
- shared detach operation;
- terminal result/cache semantics;
- stale-result rejection;
- manager/session ownership.

### 9.10 IAttachStrategy

The strategy is an OS/mechanism seam, not a second lifecycle authority.

It must support semantic operations equivalent to:

```text
begin/start attach bootstrap
invoke lifecycle entry:
    explicit detach
    orphan reconciliation
```

It receives manager-owned:

```text
AttachId
exact ProcessIdentity
requested composition
stage deadline
manager-owned cleanup lifetime
```

It returns semantic outcomes, not raw mechanics:

```text
ReadyForClientConnection
AlreadyAttached
AttachInProgress
Orphaned
TargetExited
BootstrapFailure
RendezvousFailure
Settling
CleanupUnknown
```

It must not expose remote handles, remote addresses, remote thread objects, DLL paths, export addresses, or arbitrary bootstrap argument strings.

### 9.11 Composition capability mapping

For the initial semantic model:

```text
RequestedCompositionId = "winforms"
```

A successful authenticated session requires:

```text
IClrInspectionPort
adapter ID "winforms.correlation"
adapter implements IWinFormsCorrelationPort
```

A1 fakes model this directly. A1 must not infer WinForms support from arbitrary capability strings. If the current I3 wire does not already expose an exact composition capability, B0 freezes the precise mapping before B1; A1 still treats the required adapter as mandatory.

If I3 succeeds but either required surface is unavailable:

```text
CapabilityMismatch
→ no AttachSession publication
→ tracked cleanup
```

---

## 10. Authenticated I3 connector

The transport-neutral connector seam is owned by `NativeSpy.Client`.

### 10.1 Connector request

Declare a bounded semantic type conceptually equivalent to:

```text
AuthenticatedClientConnectRequest
    ExpectedTargetProcessIdentity
    Endpoint
    BootstrapAuthenticationSecret
    SupportedProtocolRange
```

The exact C# record/class names may follow repository convention, but the fields and semantics are frozen.

`ExpectedTargetProcessIdentity` is required exact `ProcessIdentityDto`. The connector independently preserves existing exact-target validation.

`Endpoint` represents the Agent Named Pipe endpoint for this connection. It is attach-internal/session-bootstrap data, bounded according to existing Named Pipe endpoint rules, and is never exposed on AttachSession.

`BootstrapAuthenticationSecret` is the existing AgentHost bootstrap nonce/authentication material. It is secret, never logged, never placed in diagnostics or AttachSession, and is retained only according to existing Host/session cleanup requirements.

`SupportedProtocolRange` is the existing protocol compatibility material needed by the connector. No new protocol negotiation is invented.

The strategy/rendezvous layer produces this semantic handoff from the existing `BootstrapDescriptor`. `NativeSpy.Attach` passes it to the connector. `NativeSpy.Client.NamedPipes` converts/uses it through the existing `NamedPipeClientSession.ConnectAsync` path.

`BootstrapDescriptorWire` is never exposed through AttachSession.

### 10.2 Connector result

The connector returns a strict result union:

```text
AuthenticatedClientConnectResult

Success:
    IAuthenticatedClientSession

Failure:
    ClientSessionConnectFailure
```

Expected connection/session-establishment problems are values. Caller cancellation remains `OperationCanceledException`; programmer/invariant misuse may throw.

Conceptual connector failure categories:

```text
TransportUnavailable
TargetClosed
ProtocolRejected
AuthenticationRejected
MalformedBootstrap
Timeout
InternalTransportFailure
```

The Named Pipe implementation translates existing expected pipe/protocol exceptions into this union. The AttachManager does not interpret concrete Named Pipe exception classes.

### 10.3 Authenticated client-session projection

`IAuthenticatedClientSession` supplies:

```text
AgentSessionId
IClrInspectionPort
EffectiveCapabilities
TryGetAdapter(...)
Task<ClientSessionTermination> Completion
IAsyncDisposable
```

`Completion` completes exactly once. `ClientSessionTermination` distinguishes at least:

```text
RemoteClosed
LocalDisposed
ProtocolFailure
TransportFailure
```

No arbitrary exception object becomes public contract data.

`LocalDisposed` is an expected local terminal reason; `RemoteClosed`, `ProtocolFailure`, and `TransportFailure` are unexpected unless an explicit detach/cleanup operation already owns the generation. Any authenticated session that terminates while still live triggers the appropriate target-side cleanup/orphan path rather than ordinary continued operation.

The manager captures:

```text
AttachId
ProcessIdentity
AgentSessionId
```

when observing completion. A stale completion callback is ignored unless all current-generation guards still match. If the authenticated client disappears while the generation is live, this completion is what starts the target-side client-loss/orphan path.

`NativeSpy.Client.NamedPipes` implements the projection by adapting the existing `NamedPipeClientSession`. The concrete transport session remains hidden.

### 10.4 Existing I3 preservation

The connector must:

- call `BootstrapDescriptorWire.EnsureMessageKind` through the existing path;
- validate exact expected ProcessIdentity through the existing path;
- preserve nonce validation;
- preserve protocol negotiation;
- preserve SID/authentication behavior;
- preserve existing `ProtocolErrorCode` and `OperationErrorCode` meanings;
- enforce the manager-supplied I3 handshake budget rather than silently substituting a default timeout.

`AttachSession` receives only the semantic projection.

### 10.5 Connector timeout enforcement

`IAuthenticatedClientSessionConnector.ConnectAsync` receives the manager-owned I3 handshake budget through the connector request or an equivalent manager-owned deadline.

The connector honors that stage budget independently of the caller CancellationToken and must not silently substitute the existing default timeout.

A connector timeout returns:

```text
ClientSessionConnectFailure.Timeout
```

which maps to:

```text
AttachFailureCode = StageTimedOut
AttachStage = I3Handshake
```

Caller cancellation remains `OperationCanceledException`.

---

## 11. Lifecycle state machine

### 11.1 Manager states

```text
Detached
Probing
Bootstrapping
Connecting
Attached
Detaching
Settling
CleanupUnknown
TargetExited
```

`ProbeAsync` standalone does not reserve or transition the manager lifecycle state.

### 11.2 State transitions

```text
Detached
    └─ valid AttachAsync allocates AttachId and enters Probing

Probing
    ├─ unsupported/access/identity failure → Detached with typed failure
    ├─ caller cancellation → OperationCanceledException, Detached
    ├─ eligible → Bootstrapping
    └─ second same-target AttachAsync → AttachInProgress before probe work

Bootstrapping
    ├─ resident reports current orphan → one internal ReconcileCurrentOrphan
    │      ├─ positive closure → continue this same AttachId into Bootstrapping
    │      └─ failure/unknown → Settling or CleanupUnknown
    ├─ strategy/rendezvous failure → Settling or CleanupUnknown
    ├─ target exit → TargetExited
    ├─ caller cancellation → tracked cleanup, no publication
    └─ validated handoff → Connecting

Connecting
    ├─ I3 success + requested capability validation → Attached
    ├─ handshake/capability failure → tracked cleanup
    ├─ target exit → TargetExited
    └─ caller cancellation → tracked cleanup, no publication

Attached
    ├─ explicit valid detach → Detaching
    ├─ pipe loss → coordinator orphan/cleanup path
    └─ target exit → TargetExited

Detaching
    ├─ positive Host closure → Detached
    ├─ target exit → TargetExited
    ├─ shared cleanup settlement expiry → CleanupUnknown
    └─ caller timeout/cancellation → caller-local observation only; shared operation continues

Settling
    ├─ positive closure → Detached
    ├─ target exit → TargetExited
    └─ cleanup-settlement budget expires without proof → CleanupUnknown

CleanupUnknown
    ├─ bounded reconciliation proves closure → Detached
    └─ target exits → TargetExited
```

### 11.3 Same-target behavior

Once an AttachManager owns the same-target operation and enters `Probing`, another AttachAsync for that exact identity returns `AttachInProgress` with its own fresh AttachId.

This is distinct from standalone ProbeAsync, which remains non-reserving and advisory.

### 11.4 Session creation point

No AttachSession exists during Probing, Bootstrapping, or Connecting.

An Agent SessionId may exist transiently after successful I3 authentication, but AttachSession is not published until requested composition and capability validation succeeds.

### 11.5 Attach success linearization

After connector success, I3 authentication, CLR-surface validation, and requested-composition adapter validation, the manager enters the same-target lifecycle gate and performs:

```text
1. verify AttachId is still current
2. verify ProcessIdentity is still current
3. verify manager operation is still current
4. perform one final caller CancellationToken check
5. atomically transition Connecting → Attached
6. publish/store AttachSession
```

Step 5 is the success linearization point.

If cancellation is observed before step 5:

```text
no AttachSession publication
OperationCanceledException
tracked cleanup
```

If cancellation occurs after step 5:

```text
AttachAsync succeeds
published AttachSession remains valid
cancellation cannot retroactively undo success
```

No code path may both publish success and throw cancellation.

---

## 12. Resident coordinator

### 12.1 Required semantic state

There is one NativeSpy resident coordinator per exact target process across the relevant bootstrap/managed loading contexts. The exact anchoring mechanism is B0/C0; a per-AssemblyLoadContext accidental coordinator is not sufficient.

Permitted bounded state:

```text
lifecycle gate
current lifecycle state
current AttachId while active/settling
exact target ProcessIdentity
current AgentHost reference while active/settling
one shared start task
one shared stop/reconcile task
current composition/readiness state
active LifecycleOwnerProof while attached
bounded terminal metadata
```

### 12.2 Forbidden state

The coordinator must not retain reusable:

```text
old ClrAgentSession
old HandleIds
old TypeIds
old MemberRefs
old continuation tokens
old framework evidence
old ObjectSpy state
old FlaUI objects
old client state
old target object references
reconnect sessions
multi-client namespaces
leases
object registries
```

After Host closure, all old generation roots are cleared. Old callback stack frames may finish naturally but cannot publish into or acquire state for a later generation.

### 12.3 AgentHost task sharing

The current AgentHost implementation is not yet task-idempotent. B1 must make all concurrent `StopAsync` callers await one shared shutdown operation.

The target coordinator's shared stop/reconcile operation is also one logical task per generation.

---

## 13. Bootstrap and lifecycle rendezvous

### 13.1 No permanent lifecycle pipe

There is no permanent lifecycle pipe, public coordinator RPC, shared-memory channel, or I3 `session.detach` operation.

The same NativeSpy-owned target-entry/bootstrap strategy seam that starts NativeSpy can later re-enter the already resident coordinator using a fresh one-use lifecycle rendezvous.

Semantic delivery path:

```text
AttachManager creates one-use endpoint + secret + operation data
    ↓
IAttachStrategy begins target lifecycle entry
    ↓
existing-process target entry/bootstrap mechanism
    ↓
resident coordinator
    ↓
coordinator validates and responds over the one-use rendezvous
```

For B1 cooperative targets, the harness invokes the equivalent semantic entry directly. For C1 native targets, the native entry mechanism implements the same semantic operation. The native mechanics remain C0.

### 13.2 Initial attach flow

1. Validate `AttachRequest`.
2. Allocate a fresh AttachId.
3. Inspect manager state and fail fast or claim the exact target operation.
4. Enter Probing.
5. Perform the fresh identity-consistent probe.
6. If eligible, enter Bootstrapping.
7. Create a fresh one-use rendezvous endpoint and token.
8. Pass minimum private bootstrap data through the strategy.
9. Strategy re-reads exact ProcessIdentity immediately before first invasive target entry.
10. Target bootstrap connects only after the exact match.
11. Rendezvous verifies OS-observed peer PID and exact target ProcessIdentity.
12. Resident coordinator admits or rejects the generation.
13. Coordinator creates AgentHost using target-derived security identity.
14. AgentHost generates its own Pipe name and bootstrap nonce.
15. Coordinator returns an internal validated handoff.
16. AttachManager invokes the injected authenticated-client connector.
17. Existing I3 handshake authenticates the Agent session.
18. Validate requested composition/capabilities.
19. Publish AttachSession only after all required validation succeeds.
20. Dispose rendezvous endpoint and clear one-use token.

### 13.3 Explicit detach delivery

Explicit detach requires:

```text
exact AttachId
exact ProcessIdentity
LifecycleOwnerProof
fresh one-use rendezvous token
```

The strategy performs lifecycle entry into the resident coordinator. The coordinator atomically validates the generation/proof/state and joins the shared stop task.

### 13.4 Orphan reconciliation delivery

Orphan reconciliation is internal to a new AttachAsync attempt and is not a public `ReconcileAsync` API.

The reconciliation is performed at most once by that attach attempt for the observed current orphan. If it positively proves closure, the same AttachId may continue with a new bootstrap rendezvous. It does not allocate a second AttachId and it does not bypass the fresh attach-owned probe/eligibility result.

A foreign manager may invoke it only after the resident coordinator has independently established client loss and entered `Orphaned/Settling`.

The operation is explicitly:

```text
ReconcileCurrentOrphan
```

It is never:

```text
StopWhateverHostExists
```

If an old AttachId is supplied, it is a stale-generation guard. If none is supplied, the coordinator binds the operation only to the generation already marked orphaned.

The coordinator atomically rejects reconciliation if a live client/session is still active or if a newer generation has started.

### 13.5 One-use semantics and invalid peers

A rendezvous endpoint and token are one-use operation authentication material, but a transport connection attempt is not itself an accepted operation.

A same-user unrelated process that opens or guesses the endpoint must not permanently consume the valid operation merely by connecting. The implementation distinguishes:

```text
transport connection attempt
authenticated operation acceptance
```

The operation is semantically consumed only after the request passes:

```text
endpoint ACL
secret validation
operation-kind validation
expected ProcessIdentity validation
current generation/state validation
```

Invalid peers are rejected. B0 may choose a bounded number of invalid transport attempts before endpoint retirement, but there is only one successfully authenticated operation and no replay after successful acceptance. Terminal operation/timeout policy clears the endpoint and token.

---

## 14. Security secrets and identity table

| Secret/identity | Creator | Scope | Transfer | Validator | Destruction |
|---|---|---|---|---|---|
| `ProcessIdentity` | OS/reader | One process incarnation | Semantic evidence only | AttachManager, rendezvous, AgentHost/I3 | Not secret; becomes stale on exit/reuse |
| `AttachId` | AttachManager | One valid explicit attach attempt/generation | Bootstrap/lifecycle correlation | Manager/coordinator generation checks | Retain only bounded terminal metadata |
| Rendezvous token | AttachManager | One bootstrap/detach/reconcile rendezvous | Private strategy/bootstrap data | Rendezvous endpoint/coordinator | Clear after terminal rendezvous outcome |
| `LifecycleOwnerProof` | Resident coordinator | Exact live AttachId + ProcessIdentity | Returned through authenticated bootstrap handoff to owning manager | Resident coordinator, constant-time comparison | Clear on detach, orphan transition, target exit, or failed publication |
| AgentHost bootstrap nonce | Target AgentHost | Descriptor/I3 handshake | Internal descriptor | AgentHost | Clear when Host/session becomes terminal |
| Controller primary SID | Controller token | Controller process security identity | Not caller supplied | Windows token/access policy | Not a secret owned by attach |
| Target primary SID | Target token | Target process security identity | Target-derived only | Target bootstrap/Host and policy | Not a secret owned by attach |

`AttachId` is never sufficient to stop a live session. `LifecycleOwnerProof` is not a lease: it has no renewal, expiration scheduler, reconnect semantics, or object-retention meaning.

---

## 15. Orphan, foreign-manager, and recovery semantics

### 15.1 Live foreign manager

A second controller cannot stop a live session by knowing:

```text
PID
ProcessIdentity
AttachId
```

It lacks the active `LifecycleOwnerProof`.

A foreign manager's AttachAsync receives:

```text
AlreadyAttached
```

or:

```text
AttachInProgress
```

depending on the current state.

### 15.2 Client loss

The resident coordinator transitions only after independent evidence that the original authenticated client is absent:

```text
Attached(A)
    → Orphaned/Settling(A)
```

It then invalidates the active LifecycleOwnerProof. A foreign manager may perform one bounded `ReconcileCurrentOrphan` through a fresh authenticated rendezvous.

### 15.3 Stale recovery

Recovery for A is rejected if:

```text
current state is live Attached(B)
current AttachId is not A
client is no longer absent
ProcessIdentity changed
```

Recovery cannot kill, replace, or reset B.

### 15.4 CleanupUnknown

`CleanupUnknown` blocks new attach until:

```text
bounded reconciliation positively proves closure
```

or:

```text
target exits
```

No force reset, StartAnyway, ReplaceCurrentHost, KillTarget, or second Host is allowed.

---

## 16. Timeout and cancellation ownership

### 16.1 AttachTimeouts

Timeout policy is manager/composition configuration, not duplicated request fields.

```text
AttachTimeouts
    Probe
    NativeProcessEntry
    BootstrapStartup
    Rendezvous
    AgentHostReadiness
    I3Handshake
    DetachCallerWait
    CleanupSettlement
    Reconciliation
```

Exact production durations are I5-F. A1 injects deterministic short budgets for tests.

Requests contain target intent. `CancellationToken` contains caller cancellation. `AttachTimeouts` contains lifecycle policy.

Every AttachTimeouts budget must be:

```text
> TimeSpan.Zero
finite
not Timeout.InfiniteTimeSpan
<= 24 hours
```

Invalid manager options are programmer-contract errors at manager construction/configuration time. Production defaults remain I5-F; A1 tests use explicit deterministic values. No stage silently borrows another stage's budget.

### 16.2 Caller cancellation

Caller-requested cancellation always propagates as:

```text
OperationCanceledException
```

This applies consistently to:

```text
ProbeAsync
AttachAsync
DetachAsync
```

Expected target/lifecycle failures remain typed results. Programmer misuse remains ordinary argument/state exceptions.

### 16.3 Cleanup token

Before side effects:

```text
caller token may stop caller-owned work
```

After side effects begin:

```text
caller cancellation
    → caller receives OperationCanceledException
    → attach does not continue toward successful publication
    → manager-owned cleanup lifetime continues
    → cleanup is not cancelled by caller token
    → same-target attach remains blocked
```

Mandatory settlement never uses the caller token as its sole cancellation authority.

No remote thread is forcibly terminated.

### 16.4 Timeout precedence

- Probe budget expiry is a typed probe/attach timeout failure; no target side effect exists.
- Bootstrap/rendezvous/handshake stage budget expiry begins manager-owned cleanup.
- `DetachCallerWait` expiry affects only that caller and returns a non-authoritative wait-timeout observation; it is not cached as the target terminal detach result.
- Caller cancellation always throws `OperationCanceledException` and is not cached.
- `CleanupSettlement` expiry without positive closure changes shared lifecycle state to `CleanupUnknown`.
- Reconciliation budget expiry leaves the target blocked in `CleanupUnknown` unless target exit is positively established.

### 16.5 Settling versus CleanupUnknown

**Settling** means a known manager/coordinator-owned cleanup or stop operation exists and is still in progress.

**CleanupUnknown** means positive closure has not been established and the shared cleanup has lost a trustworthy completion path or exceeded its manager-owned cleanup-settlement budget without proof.

A caller-local timeout alone never changes Settling to CleanupUnknown while shared cleanup remains within its policy.

Transitions:

```text
Settling → Detached
    positive Host/session closure established

Settling → CleanupUnknown
    cleanup settlement budget expires without proof
    or shared cleanup loses authoritative completion path

CleanupUnknown → Detached
    bounded reconciliation positively proves closure

CleanupUnknown → TargetExited
    exact target process exits
```

---

## 17. Failure taxonomy and strict result unions

### 17.1 Attach failure codes

A1 freezes these outer attach-domain codes:

```text
TargetNotFound
TargetExited
TargetIdentityChanged
AccessDenied

UnsupportedArchitecture
UnsupportedRuntime
UnsupportedComposition
ProbeInconclusive

AlreadyAttached
AttachInProgress

StageTimedOut

PreEntryValidationFailed
BootstrapFailed
RendezvousFailed
RendezvousPeerVerificationFailed
ClientSessionEstablishmentFailed
CapabilityMismatch

CleanupFailed
CleanupUnknown

InternalFailure
```

The outer set does not duplicate lower-layer authoritative meanings. In particular, there are no separate outer `ProtocolMismatch` or `AuthenticationFailed` codes. Those are preserved as underlying `ProtocolErrorCode` evidence under `ClientSessionEstablishmentFailed` at `I3Handshake`.

`UnsupportedTargetLayout` is not an A1 public probe dimension or eligibility rule. Later C/D strategy/payload policy may define a later-frozen compatibility result if required.

### 17.2 AttachStage

```text
Probe
PreEntryValidation
NativeProcessEntry
BootstrapStartup
Rendezvous
AgentHostReadiness
I3Handshake
CapabilityValidation
Detach
Reconciliation
Cleanup
```

### 17.3 Existing lower-layer errors

`ProtocolErrorCode` remains authoritative after I3 begins.

`OperationErrorCode` remains authoritative for Agent operations.

Attach wraps, rather than replaces, these lower-layer meanings.

### 17.4 Strict result unions

`AttachResult` has exactly one valid branch:

```text
Success(AttachId, AttachSession)
Failure(AttachId, AttachFailure)
```

`TargetProbeResult` has a completed branch or a terminal probe-outcome branch. Caller cancellation is absent because it throws.

`DetachResult` contains either one authoritative lifecycle outcome or the explicitly non-authoritative caller-wait observation defined in §9.8, with its required bounded failure data. Invalid combinations such as success plus failure, or failure without a failure code, are not constructible through public factories.

### 17.5 Canonical failure-to-state mapping

| Condition | Outer code/stage | Post-failure lifecycle |
|---|---|---|
| Fresh probe finds no process | `TargetNotFound` / `Probe` | `Detached` |
| Target disappears after prior observation | `TargetExited` / relevant stage | `TargetExited` |
| PID identifies another incarnation | `TargetIdentityChanged` / relevant stage | `Detached` or `TargetExited` as proven |
| Required access/security evidence denied before side effects | `AccessDenied` / relevant stage | `Detached` |
| Pre-entry identity reread denied | `AccessDenied` / `PreEntryValidation` | `Detached` |
| Pre-entry identity reread cannot establish a trustworthy match | `PreEntryValidationFailed` / `PreEntryValidation` | `Detached` |
| Unsupported architecture | `UnsupportedArchitecture` / `Probe` | `Detached` |
| Unsupported runtime | `UnsupportedRuntime` / `Probe` | `Detached` |
| Valid but unsupported composition | `UnsupportedComposition` / `Probe` | `Detached` |
| Unknown eligibility | `ProbeInconclusive` / `Probe` | `Detached` |
| Same target already attached | `AlreadyAttached` / `PreEntryValidation` | Existing lifecycle unchanged |
| Same target transient lifecycle | `AttachInProgress` / `PreEntryValidation` | Existing lifecycle unchanged |
| Stage budget expires before side effects | `StageTimedOut` / current stage | `Detached` |
| Stage budget expires after side effects | `StageTimedOut` / current stage | `Settling` or `CleanupUnknown` after cleanup policy |
| Rendezvous peer cannot be trusted | `RendezvousPeerVerificationFailed` / `Rendezvous` | Tracked cleanup |
| Connector transport/session failure | `ClientSessionEstablishmentFailed` / `I3Handshake` | Tracked cleanup |
| I3 protocol rejection | `ClientSessionEstablishmentFailed` / `I3Handshake` plus exact `ProtocolErrorCode` | Tracked cleanup |
| Requested composition/adapter absent after I3 | `CapabilityMismatch` / `CapabilityValidation` | Tracked cleanup |
| Cleanup loses authoritative completion | Original causal code retained | `PostFailureLifecycle = CleanupUnknown` |

The original causal failure is retained. Cleanup uncertainty does not replace every earlier failure with `CleanupUnknown`.

`AttachFailure` carries a bounded `PostFailureLifecycle` value when side effects occurred:

```text
Detached
Settling
CleanupUnknown
TargetExited
```

---

## 18. Public diagnostics

Public diagnostics are bounded and allowlisted.

Conceptual shape:

```text
AttachDiagnostic
    DiagnosticId
    Win32ErrorCode?
    HResult?
    Architecture?
    RuntimeFamily?
    RuntimeVersion?
    ProtocolErrorCode?
    OperationErrorCode?
```

`DiagnosticId` is NativeSpy-owned ASCII data only:

```text
maximum 64 characters
allowed: A-Z, a-z, 0-9, '.', '_', '-'
no target-provided text
```

Probe limitations are fixed enum values, not free-form text, with a maximum of 16 entries per result.

Never expose publicly:

```text
Exception.ToString()
exception stack traces
arbitrary exception messages
command line
environment
module/file paths
remote addresses
memory contents
target-provided strings
arbitrary native loader text
```

Existing protocol/operation error codes may be carried as bounded enums.

---

## 19. Partial-attach cleanup matrix

| Stage | Resources | Lifecycle cleanup owner | Mandatory action | Failure/result | Legal residue | Secret destruction | Reattach legality |
|---|---|---|---|---|---|---|---|
| Request validated/AttachId allocated | Manager attempt metadata | Manager | Record terminal failure if operation is rejected before claim | Typed failure with `PostFailureLifecycle = Detached` | Bounded AttachId metadata | No rendezvous secret exists | Allowed if no operation is active |
| Fresh probe started | Local process/module/token observations | Probe component | Dispose local observations; reread identity before completion | `TargetExited`, `TargetIdentityChanged`, `AccessDenied`, or typed probe result | None | None | Allowed unless another operation exists |
| Probe completed | Immutable probe result | Manager | Do not invoke strategy unless `Eligible` | Unsupported/Unknown/Access failure; lifecycle Detached | Result metadata only | None | Allowed after fresh attempt |
| Rendezvous endpoint/token created | Controller endpoint and one-use token | Strategy | Close endpoint; clear token on terminal strategy outcome | `RendezvousFailed` or `StageTimedOut`; tracked cleanup if entry began | None | Clear endpoint/token | Blocked until tracked outcome resolves |
| Pre-entry identity reread | Local strategy resources | Strategy | Re-read exact identity immediately before first invasive action; close local resources on mismatch | `TargetExited`, `TargetIdentityChanged`, `AccessDenied`, or `PreEntryValidationFailed` | None | Clear operation token | Allowed after Detached |
| Process entry begun before coordinator admission | Native-entry resources | Strategy | Stop/rollback strategy-owned entry resources; C0 defines exact native mechanics | `BootstrapFailed`, `StageTimedOut`, or target-exit result; Settling/Unknown if cleanup cannot be proven | Only later-authorized bootstrap residue | Clear rendezvous token; clear other strategy secrets | Blocked until closure |
| Target bootstrap reached but generation not admitted | Bootstrap/coordinator attempt | Strategy | Complete failed handoff cleanup; do not admit second generation | `BootstrapFailed`/`RendezvousFailed`; target-side expiry may be required | Bounded bootstrap infrastructure only | Clear rendezvous token | Blocked until Detached |
| Generation admitted | Resident generation, AttachId, LifecycleOwnerProof | Resident coordinator | Retire generation through shared stop/reconcile operation on failure | Original causal failure plus Settling/Unknown lifecycle | No old session namespace | Clear proof on retirement | Only after Detached |
| AgentHost constructed | Host/options/nonce/composition | Resident coordinator | Invoke one shared Host stop operation; AgentHost performs Host-owned teardown | `BootstrapFailed` or Settling/Unknown | No public descriptor | Clear nonce at terminal Host state | Blocked until positive closure |
| AgentHost started | Pipe/server/composition | Resident coordinator | Own one shared stop task; AgentHost executes its resource teardown | Settling while active; CleanupUnknown if closure loses proof | Host may remain only under Settling/Unknown | Clear nonce at terminal Host state | Blocked |
| Descriptor produced | Internal descriptor/handoff | Resident coordinator | Return only through authenticated rendezvous; invalidate on failed handoff | `BootstrapFailed`/`RendezvousFailed`; descriptor never public | No public descriptor | Clear rendezvous token | Blocked until cleanup |
| I3 connection begun | Client transport and pending handshake | Connector | Dispose local connection/session-establishment resources | `ClientSessionEstablishmentFailed` or `StageTimedOut` | No public client session | Connector clears local handoff; Agent nonce follows Host policy | Target cleanup row applies when generation was admitted |
| I3 connection failure after generation admission | Admitted target Host/session plus failed client connection | Resident coordinator | Stop admitted target generation through shared stop operation | Original connector failure retained; Settling/Unknown until closure | No public client session | Coordinator clears proof/nonce at retirement | Blocked until closure |
| I3 authenticated, capability validation pending | Agent SessionId/registries plus client projection | Resident coordinator | Manager withholds AttachSession publication; coordinator runs target-generation cleanup | `CapabilityMismatch`; original failure retained | No public partial session | Clear private handoff; coordinator clears proof at retirement | Blocked until closure |
| AttachSession published | Active authenticated session | Resident coordinator | Accept explicit detach or client-loss cleanup; manager controls publication only | Active/Attached | Current generation only | Proof retained privately while active | Only after Detached |
| Caller cancels after side effects | Current stage resources | Manager | Do not use caller token for mandatory cleanup; prevent publication | `OperationCanceledException` to caller; Settling or Unknown lifecycle | Allowed residue by current stage | Clear secrets at terminal cleanup | Blocked |
| Explicit detach claimed | Active Host/session | Resident coordinator | One shared stop/reconcile task; manager callers join | `Detached`, `TargetExited`, `CleanupUnknown`, or non-terminal `DetachFailed` | No second detach operation | Proof used then cleared at terminal transition | Only after Detached |
| Authenticated client completion/pipe loss | Client absent plus active target generation | Resident coordinator | Mark orphan only after independent client-loss evidence; begin cleanup | Orphaned → Settling; then Detached/Unknown/TargetExited | Bounded coordinator | Invalidate proof at orphan transition | Foreign recovery only after orphan state |
| Host close begins | Host/composition/session | Resident coordinator | Coordinator owns stop operation; AgentHost performs Host teardown | Settling until positive closure | No new generation | Clear nonce when Host terminal |
| Host closure verified | No active session resources | Resident coordinator | Clear old roots and publish Detached | Authoritative Detached | Bounded terminal metadata | Clear proof/tokens | Allowed |
| Host stop failure/settlement expiry | Uncertain Host/session closure | Resident coordinator | Transition CleanupUnknown; do not force reset | Original causal failure plus authoritative CleanupUnknown | No force reset | Clear invalid operation secrets | Blocked |
| CleanupUnknown reconciliation | Positive closure observation | Reconciliation operation | Perform one bounded current-orphan/unknown reconciliation | Detached on proof; otherwise remain CleanupUnknown | Bounded coordinator | Clear reconciliation token | Allowed only after Detached |
| Target exits | Process gone | Resident coordinator | Terminalize generation and clear generation roots | Authoritative TargetExited | No target resources | Clear all generation secrets | New ProcessIdentity only |

“Best effort” is not a lifecycle result. If required cleanup cannot be positively confirmed within manager-owned policy, the state is `CleanupUnknown`.

---

## 20. Detach, Dispose, and races

### 20.1 Successful detach

`Detached` is authoritative only after:

```text
AgentHost shutdown is positively verified
ClrAgentSession is closed
composition is disposed
Agent session endpoint is closed
old resident generation roots are cleared
application process remains alive
```

Complete unloading of every NativeSpy native/managed module is not promised.

Already-started target callbacks may finish naturally. They cannot publish into or acquire state for a newer generation.

### 20.2 Shared detach task

For one exact AttachId:

```text
first valid DetachAsync → creates shared authoritative detach task
concurrent valid DetachAsync → joins that same task
```

Each caller has an independent caller token and `DetachCallerWait` budget.

Caller timeout/cancellation is not cached as the authoritative target result.

Successful authoritative `Detached` is cached for later valid calls on the still-owned, not-yet-disposed session.

### 20.3 Dispose/Detach linearization

AttachSession has manager-controlled local states conceptually equivalent to:

```text
Active
DetachClaimed
LocallyDisposed
Terminal
```

The exact private implementation may vary, but ownership transitions are synchronized by the manager/session gate.

#### Detach claims first

```text
Active → DetachClaimed
```

is the detach linearization point. After that:

```text
AttachSession is operationally terminal
lifecycle never returns to Attached
DisposeAsync does not independently close the transport
DisposeAsync joins/awaits manager-controlled local session cleanup
DisposeAsync cannot cancel or replace authoritative detach
```

#### Dispose claims first

```text
Active → LocallyDisposed
```

is the disposal linearization point. It:

```text
closes local authenticated client resources
makes operational surfaces terminal
may cause pipe-loss target cleanup
```

A later `DetachAsync(disposedSession)` is programmer/state misuse and throws.

#### Repeated DisposeAsync

Idempotent.

There is no ambiguous winner rule: exactly one synchronized transition claims ownership first.

### 20.4 Manager disposal

`IAttachManager` is `IAsyncDisposable`.

`DisposeAsync` atomically marks the manager closed to new public operations. After that, new `ProbeAsync`, `AttachAsync`, and `DetachAsync` calls throw `ObjectDisposedException`.

For in-progress work:

```text
probe/pre-side-effect attach
    → cancel local work

side-effecting unpublished attach
    → prevent publication
    → retain manager-owned tracked cleanup

active published session
    → locally terminalize and close authenticated client resources
    → do not claim authoritative detach

detach already claimed
    → do not cancel shared authoritative detach/cleanup
```

Manager disposal may await bounded local/cleanup obligations using `CleanupSettlement`, but manager disposal is not a `DetachResult`. Manager-owned settlement operation objects remain rooted until bounded settlement completes.

### 20.5 Concurrent DisposeAsync

`DisposeAsync` is idempotent. It cannot free the target gate by assumption or report successful detach.

### 20.6 Target exit race

Target exit produces `TargetExited` when positively established. It does not become successful application-preserving detach.

### 20.7 Old A after B exists

A's cached terminal detach result may be returned only for valid calls against A's still-owned, not-disposed session. It cannot affect B.

---

## 21. Concurrent attach and foreign-manager behavior

### 21.1 Manager-local gate

Per-target gates are keyed by exact value equality, not object reference and not PID alone.

Valid AttachAsync ordering:

```text
validate request
→ allocate AttachId
→ inspect manager state
→ if same-target operation exists, return typed fail-fast result
→ otherwise acquire and enter Probing
```

### 21.2 Target-side gate

The resident coordinator owns one process-global semantic gate. It serializes:

```text
start
stop
orphan transition
orphan reconciliation
```

The implementation primitive may be a lock/CAS/event/task structure selected later. A global named mutex is not required by this architecture.

### 21.3 Race table

| Race | Result |
|---|---|
| Two valid AttachAsync calls before either probes | First claims operation; second gets `AttachInProgress` with its own AttachId |
| Standalone ProbeAsync during AttachAsync | Allowed; advisory only |
| AttachAsync during manager-owned Probing | `AttachInProgress` |
| AttachAsync during Bootstrapping/Connecting | `AttachInProgress` |
| AttachAsync during Attached | `AlreadyAttached` |
| AttachAsync during Detaching/Settling/Unknown | `AttachInProgress` |
| Two different exact target identities | Independent operations |
| Foreign manager against live A | `AlreadyAttached`/`AttachInProgress`; no takeover |
| Foreign manager after independently observed orphan A | One internal orphan reconciliation may run |
| Orphan recovery after B begins | Reject stale A; B remains untouched |
| Manager B uses session owned by A | Ordinary contract/state exception |
| A detach arrives after B is published | Stale generation rejection |

---

## 22. Re-attach and late callbacks

Re-attach is explicit and creates:

```text
new AttachId
new resident generation
new AgentHost
new ClrAgentSession
new Agent SessionId
new HandleIds/generations
new TypeIds
new MemberRefs
new adapters
```

No old identity revives.

Already-started callbacks from A may mutate the inspected application's own state according to application code. NativeSpy guarantees only that A callbacks cannot:

```text
respond into B
create B handles
mutate B registries
use B transport
acquire/release B lifecycle gate
be interpreted as B
```

No forced callback termination is attempted.

---

## 23. I3 preservation and security sequence

### 23.1 Bootstrap handoff

The attach rendezvous authenticates the target bootstrap handoff using:

```text
one-use rendezvous token
same-user ACL
OS-observed peer PID
exact ProcessIdentity
AttachId/current operation
bounded message schema
```

It does not replace I3.

### 23.2 I3 connection

After handoff:

```text
existing NamedPipeClientSession.ConnectAsync
    → descriptor message-kind validation
    → exact descriptor identity validation
    → Named Pipe connection
    → HelloRequestWire with expected identity + nonce
    → AgentHost identity/nonce/SID/protocol validation
    → HelloResponseWire
```

No `session.detach` operation is added.

### 23.3 Rendezvous peer-PID ownership

`NativeSpy.Transport.NamedPipes` owns only the narrow OS transport primitive conceptually equivalent to:

```text
Try/GetConnectedProcessId
    backed by GetNamedPipeClientProcessId
```

It reports only the OS-observed connected-process identity fact. It must not know:

```text
AttachId
expected attach target
lifecycle state
authorization policy
```

`NativeSpy.Attach.Windows` owns attach/security interpretation:

```text
observed peer PID
→ re-read exact ProcessIdentity
→ compare with expected ProcessIdentity
→ fail closed
```

For every production rendezvous:

```text
GetNamedPipeClientProcessId succeeds
→ exact ProcessIdentity reread succeeds
→ identity equals expected target
→ continue
```

Any failure in that chain fails rendezvous peer verification. A self-reported PID or identity message never substitutes for OS-observed peer identity.

The primitive is B1 implementation work, not A1 production work.

Current official API reference:

<https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid>

### 23.4 Security threats

| Threat | Mitigation |
|---|---|
| Guessed endpoint | Random bounded endpoint plus restrictive ACL |
| Guessed token | Cryptographically random one-use token and constant-time comparison |
| Same-user unrelated process | OS peer PID → exact ProcessIdentity |
| PID reuse | Exact process-start identity at every stage |
| Replay | One-use endpoint/token and terminal disposal |
| Stale response | AttachId/current-operation checks |
| Live takeover | LifecycleOwnerProof plus exact generation |
| Orphan takeover of B | Current orphaned-state/generation CAS |
| Higher integrity target | Initial policy rejects; no elevation |
| Descriptor spoofing | Descriptor checks followed by existing I3 identity/nonce/SID checks |
| Malformed input | Bounded strict rendezvous protocol |

Relevant Windows documentation:

- <https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid>
- <https://learn.microsoft.com/windows/win32/ipc/named-pipe-security-and-access-rights>
- <https://learn.microsoft.com/windows/win32/secauthz/mandatory-integrity-control>

---

## 24. ObjectSpy, WinForms, and FlaUI boundaries

### 24.1 ObjectSpy

ObjectSpy eventually consumes:

```text
TargetProbeResult
AttachSession
exact ProcessIdentity
IClrInspectionPort
requested composition
capabilities
exact adapter lookup
typed lifecycle failures
```

It does not know native entry, runtime hosting, remote memory, or bootstrap mechanics.

### 24.2 WinForms

A1 does not prove arbitrary-process WinForms availability. `CompositionSupport = Unknown` is correct when evidence is unavailable.

E0 must define:

- UI-thread discovery;
- execution-context selection;
- multiple UI-thread policy;
- dispatch anchor/context lifecycle;
- handle creation/recreation;
- disposed-context retirement;
- HWND-to-context routing.

E1 implements it.

No A1 API may expose `Control`, `Form`, HWND identity, or raw handles in generic attach contracts.

### 24.3 FlaUI

I5-E1 is the exact implementation iteration that replaces PID-only FlaUI target binding with exact ProcessIdentity validation in the integrated ObjectSpy flow.

E0 specifies the seam; E1 implements:

```text
exact process identity check before UIA binding
exact process identity revalidation as required
AttachId/ObjectSpy generation stamps
terminalization on target restart/exit
```

FlaUI remains an MTA-owned UIA session. No UIA2 or ProviderAware work is included.

### 24.4 Independent generation guards

AttachId does not replace ObjectSpy's independent local generations:

```text
AttachId
    attach/session generation

ObjectSpy external-selection generation
    finder/selection/preview stale-work guard

ObjectSpy CLR navigation epoch
    CLR browser/navigation stale-work guard

Agent SessionId
    authenticated Agent session identity

CLR HandleId/generation
    object identity inside Agent session
```

Required asynchronous guard matrix:

| Work/result | Required guard |
|---|---|
| Attach manager callback/result | AttachId + ProcessIdentity + current manager operation |
| Authenticated-session terminal callback | AttachId + ProcessIdentity + AgentSessionId |
| Adapter callback | AttachId + authenticated client-session identity |
| CLR operation/result | Existing Agent SessionId + HandleId/generation |
| ObjectSpy external UIA result | Existing external-selection generation |
| ObjectSpy CLR navigation result | Existing CLR navigation epoch + current AttachId where session continuity matters |
| Late target callback from A | Old Agent/session context only; cannot publish into B |

AttachId does not replace the ObjectSpy external-selection generation or CLR navigation epoch. These identities remain separate.

### 24.5 Complete identity guard matrix

| Boundary | Required guards |
|---|---|
| Probe evidence commit | ProcessIdentity before/after evidence |
| Pre-invasive strategy entry | Exact ProcessIdentity reread immediately before first invasive action |
| Rendezvous peer | OS peer PID → exact ProcessIdentity reread and comparison |
| Strategy result commit | AttachId + ProcessIdentity + current manager operation |
| Connector result commit | AttachId + ProcessIdentity + current manager operation |
| Session publication | AttachId + ProcessIdentity + current manager operation + final cancellation check |
| Authenticated-session terminal callback | AttachId + ProcessIdentity + AgentSessionId |
| Adapter callback | AttachId + authenticated client-session identity |
| CLR operation/result | Existing Agent SessionId + HandleId/generation |
| ObjectSpy external UIA result | Existing external-selection generation |
| ObjectSpy CLR navigation result | Existing CLR navigation epoch + current AttachId where session continuity matters |
| Detach/reconcile | Exact ProcessIdentity + exact/current generation and ownership rules |
| Late target callback from A | Old Agent/session context only; cannot publish into B |

AttachId does not replace ObjectSpy external selection generation or CLR navigation epoch.

---

## 25. AgentHost shutdown debt and truthful closure

### 25.1 Current behavior

**REPOSITORY FACT:** current `StopAsync` is state-idempotent but not task-idempotent. Multiple callers can enter shutdown work after `Closing` is set.

Current behavior must not be described as already fixed.

Current pre-handshake behavior must also not be rewritten by assumption: an unrelated failed pre-handshake client may leave AgentHost listening according to current policy until bootstrap expiry, explicit stop, successful client, or another terminal Host condition.

### 25.2 B1 required behavior

B0 freezes the exact API/result shape; B1 implements:

```text
one shared StopAsync logical task
one shutdown body
all callers await the same task
one-time disposal of Host-owned resources
truthful successful closure
truthful close failure/unknown result
```

AttachManager's own I3 handshake failure explicitly initiates lifecycle cleanup through the frozen lifecycle-entry path.

Required semantic distinction:

```text
HostClosedVerified
    all required NativeSpy-owned Host/session/server/composition teardown completed

HostCloseFailed / CleanupUnknown
    positive closure cannot be established
```

`State == Closed` alone is not enough if required disposal failures were swallowed.

---

## 26. Pre-I3 controller disappearance

If target-side NativeSpy has admitted generation A and started AgentHost but no authenticated I3 session has been successfully published, controller disappearance is not an attached orphan session.

It is an unpublished generation requiring cleanup:

```text
Bootstrapping/Connecting(A)
    → Settling(A)
```

The coordinator owns cleanup. Cleanup may be triggered by:

```text
explicit manager lifecycle cleanup while the controller exists
bootstrap/Host bounded expiry after controller disappearance
another existing Host terminal condition
```

AgentHost's current pre-handshake retry/listening behavior is not permission to admit a second generation. The resident lifecycle gate remains held until:

```text
HostClosedVerified → Detached
```

or:

```text
cleanup cannot be positively established → CleanupUnknown
```

Target exit clears the generation. A new attach during this period receives `AttachInProgress` or `CleanupUnknown` as appropriate.

---

## 27. Payload/runtime boundaries

### 27.1 A1

A1 reports stable facts only:

```text
architecture
runtime identity/support
requested composition support
security support
```

It must not add public layout/payload compatibility heuristics.

### 27.2 C0/C1

C0 freezes the x64/.NET 10 native entry strategy, including exact process-entry rights and cleanup ownership. C1 implements only that slice.

### 27.3 D0/D1

D0 freezes broader runtime-compatible payload selection. D1+ implements the selected strategy.

Possible future strategies include multi-target payloads, runtime-specific payloads, or another evidence-supported arrangement. A1 must not select among them.

### 27.4 Payload descriptor

A later internal payload descriptor may include:

```text
artifact identity
NativeSpy version
architecture
runtime family/version band
requested composition
protocol range
```

It is not an A1 public probe dimension and must not expose arbitrary file paths through AttachSession.

---

## 28. Compatibility matrix

| Target | A1 status | Before I5 closes | Beyond I5 |
|---|---|---|---|
| Windows x64, .NET 10 CoreCLR, WinForms | Fake semantic path; real proof later | C1/E1 required | — |
| x64 older modern .NET | Explicit non-initial support | D0/D1 policy and implementation | — |
| x86 modern .NET | Reject/defer | Not required | I5-G |
| .NET Framework | Reject/defer | Not required | I5-G |
| ARM64 | Reject/defer | Not required | I5-G |
| higher-integrity target | KnownUnsupported security policy | Negative validation | No automatic elevation |
| different-user target | KnownUnsupported security policy | Negative validation | No automatic broker |
| no readable target token | AccessDenied | Negative validation | Product policy only |
| native/AOT-only | Unsupported when reliably established; otherwise Unknown | Negative validation | Future only if designed |
| single-file/self-contained | No A1 heuristics | D0 policy | Later if supported |
| multiple WinForms UI threads | Not proven | E0/E1 | — |
| PID reuse/restart | A1 tests required | Required | — |
| launch-time attach | Not supported | Not required | I5-G |

---

## 29. Testing architecture

### 29.1 A1 semantic tests

A1 must use deterministic `TaskCompletionSource`, barriers, events, semaphores, and fake gates rather than arbitrary sleeps.

Required tests include:

```text
equal-valued distinct ProcessIdentityDto instances
reference-distinct identity map keys
PID reuse during evidence collection
process disappearance during evidence collection
AttachId allocation before same-target fail-fast
fresh AttachId for every valid failed AttachAsync
invalid request does not allocate AttachId
standalone ProbeAsync during attach
AttachAsync during manager-owned Probing
neutral probe CompositionSupport = NotRequested
neutral probe OverallAttachEligibility = Unknown
composition-specific Unknown is not eligible
fake composition-specific Supported is eligible
blank/invalid composition ID
same/different primary SID policy
higher-integrity policy through abstractions/fakes
probe AccessDenied versus optional Unknown evidence
caller cancellation as OperationCanceledException
manager-owned cleanup token after side effects
no background successful publication after cancellation
Settling → Detached
Settling → CleanupUnknown
CleanupUnknown reconciliation
same/different target concurrency
foreign-manager session misuse
Dispose before detach
Dispose racing detach
repeated DisposeAsync
old A detach after B exists
caller detach timeout then shared success
shared detach task identity
strict result-union construction
bounded diagnostics
stale A results versus B
orphan recovery cannot affect live session
stale reconciliation A cannot affect B
fresh composition-specific probe on every AttachAsync
previous ProbeAsync result cannot authorize attach
Unknown eligibility does not invoke strategy
KnownUnsupported does not invoke strategy
restart after successful probe but before invasive entry
pre-entry PID reuse
pre-entry identity-read access failure
peer PID unavailable fails closed through a B1 contract fake
connector success/failure strict unions
adapter exact lookup and terminal behavior
client-session completion callback stale-generation rejection
cancellation immediately before publication
cancellation immediately after publication linearization
Dispose wins race with Detach
Detach wins race with Dispose
manager disposal during Probe
manager disposal during side-effecting Attach
manager disposal with active session
manager disposal while Detach already claimed
pre-I3 controller disappearance semantic state
detach racing authenticated pipe loss
protocol error canonical outer mapping
capability mismatch after I3
AttachFailure PostFailureLifecycle mapping
caller timeout then shared cleanup success
successful reattach proves fresh AttachId, AgentSessionId, adapter instances, and fake CLR session identity
identity guard matrix behavior
finite timeout option validation
structured runtime-version bounds
```

### 29.2 Windows probe tests

`NativeSpy.Attach.Windows.Tests` covers:

```text
process architecture evidence
process identity reread/equality
controller primary SID derivation abstraction
[target] primary SID derivation abstraction
same SID policy
different SID policy
higher target integrity policy
inaccessible token/process mapping
no PID-only fallback
```

Use fakes/abstractions for difficult security combinations. A1 must not require destructive or elevated integration setup to test manager semantics.

### 29.3 Later tests

B1 proves cooperative no-stdout rendezvous, peer PID validation, lifecycle entry, Host closure, and re-attach.

C1 proves real native entry only for Windows x64/.NET 10 CoreCLR.

E1 proves arbitrary WinForms context and exact FlaUI identity integration.

---

## 30. I5 iteration plan

### I5-A0

This document only. Freeze contracts and ownership.

### I5-A1

Implement only:

```text
NativeSpy.Attach
NativeSpy.Attach.Windows
A1 test projects
semantic DTOs/results/unions
identity comparer
AttachId ordering
composition validation
AttachTimeouts
probe/support/security model
AttachManager
IAttachStrategy
fake strategy
fake authenticated connector/session
manager lifecycle
same-target concurrency
tracked cleanup with fakes
shared detach
foreign-session contract checks
read-only Windows probe
bounded diagnostics
```

### I5-B0

Freeze cooperative rendezvous wire/entry semantics, peer PID use, lifecycle-owner proof transfer, orphan reconciliation, Host close result, and no-stdout bootstrap.

### I5-B1

Implement cooperative target bootstrap and task-idempotent AgentHost shutdown. No native entry.

### I5-C0/C1

Freeze and implement native process entry for only x64/.NET 10 CoreCLR.

### I5-D0/D1+

Freeze and implement broader runtime-compatible payload strategy.

### I5-E0/E1

Freeze and implement arbitrary-process WinForms execution contexts and exact FlaUI integration.

### I5-F

Hardening, stress, privilege/integrity policy, diagnostics, and lifecycle race validation.

### I5-G

Optional x86, .NET Framework, ARM64, and launch-time strategies.

---

## 31. Existing debt disposition

| Debt | Disposition |
|---|---|
| AgentHost StopAsync not task-idempotent | Must fix in B1 |
| AgentHost swallowed/ambiguous close failures | B0 freezes truthful result; B1 implements |
| FlaUiFrozenSelection release | Must fix in E1 |
| ObjectSpy concrete FlaUI coupling | Must fix in E1 |
| Stateless member-page reconstruction cost | Safe to defer beyond I5 |
| Mixed-DPI manual validation | Safe to defer beyond I5 |

---

## 32. Prohibited scope

I5-A1 must not implement or introduce:

```text
real injection
native DLLs
remote memory allocation/writes
remote threads
native process entry
real rendezvous
resident coordinator
LifecycleOwnerProof target implementation
AgentHost changes
I3 redesign
ObjectSpy integration
FlaUI changes
arbitrary WinForms dispatch
WPF
writes
invocation
locator generation
bindings
collection browsing
UIA2
ProviderAware
leases
multi-client sessions
reconnect
automatic elevation
broker/service process
kernel driver
profiler/debugger architecture
shared-memory RPC
permanent lifecycle RPC
plugin registry
DI framework
Snoop
force-reset
StartAnyway
ReplaceCurrentHost
KillTarget
```

The following I4 invariants remain unchanged:

```text
Exact correlation hard gate
weak handles
strict ownership
runtime-boundary identity
host timeout authority
client abandonment semantics
malformed protocol rules
Named Pipe authentication
no reconnect
no automatic object reacquisition
```

---

## 33. Remaining owner decisions

Remaining owner decisions for I5-A1:

```text
none
```

Intentionally deferred decisions:

| Decision | Owner iteration |
|---|---|
| Cooperative rendezvous wire and Host closure result | B0 |
| Native process-entry mechanics and rights | C0 |
| Broader runtime/payload strategy | D0 |
| Arbitrary WinForms execution-context evidence/policy | E0 |
| Production timeout defaults and hardening | F |
| x86/.NET Framework/ARM64/launch strategies | G |

A1 may not select any deferred decision.

---

## 34. Frozen invariants

1. ProcessIdentity equality is PID equality plus ordinal ProcessStartIdentity equality.
2. ProcessIdentity maps never use reference equality.
3. A real probe rereads ProcessIdentity after evidence collection.
4. Evidence across an identity mismatch is discarded.
5. Every process-entry strategy rereads ProcessIdentity immediately before its first invasive target side effect.
6. No invasive target operation occurs unless that pre-entry identity matches exactly.
7. Every valid AttachAsync allocates AttachId before lifecycle inspection.
8. Invalid programmer input allocates no AttachId.
9. AttachId is fresh, opaque, non-reused, and not secret.
10. Standalone ProbeAsync is non-reserving.
11. AttachAsync-owned Probing reserves the same-target lifecycle operation.
12. A second same-target AttachAsync during Probing returns AttachInProgress.
13. Attached returns AlreadyAttached.
14. Neutral composition probe returns NotRequested and Unknown overall eligibility.
15. Only `winforms` is initially recognized as supported; valid other IDs are KnownUnsupported.
16. Unknown composition support is not attach-eligible.
17. A1 has no public TargetLayoutSupport dimension.
18. Composition IDs are exact bounded lowercase ASCII identifiers.
19. Caller cancellation is OperationCanceledException.
20. Mandatory cleanup uses manager-owned cancellation/lifetime.
21. Settling and CleanupUnknown are distinct.
22. CleanupUnknown blocks new attach.
23. No force reset or second Host exists.
24. Initial security policy requires same primary SID and target integrity not higher than controller.
25. Target AgentHost derives AllowedUserSid from the target primary token.
26. A lifecycle proof is required for live explicit detach.
27. Foreign managers cannot take over a live session.
28. Orphan reconciliation requires independent client-loss evidence.
29. Orphan reconciliation is current-orphan-only, never stop-whatever.
30. I3 remains the authenticated session protocol.
31. The I3 connector seam belongs to NativeSpy.Client.
32. The connector handoff, result union, session projection, and terminal completion are bounded contracts.
33. AttachSession never exposes transport/descriptors/secrets.
34. `winforms` success requires `IClrInspectionPort` and `winforms.correlation`.
35. DisposeAsync is local-only and idempotent.
36. Detach is authoritative and task-idempotent per AttachId.
37. DetachFailed is non-terminal; its lifecycle is Settling or CleanupUnknown.
38. Caller detach timeout/cancellation is not cached as the authoritative result.
39. A successful detach requires positive Host/session closure.
40. A target exit is not successful application-preserving detach.
41. Old callbacks cannot affect a later generation.
42. AttachId does not replace ObjectSpy selection or navigation epochs.
43. Reattach creates fresh identities and registries.
44. No Snoop concepts cross any contract.
45. A1 does not implement attach/injection.
46. A1 does not claim real CoreCLR compatibility from module filename/version alone.
47. All timeout budgets are finite, positive, and at most 24 hours.
48. Every valid AttachAsync performs a fresh composition-specific probe.
49. Unknown/unsupported probe results never invoke the strategy.
50. A1 connector/session/adapter tests use deterministic synchronization.

---

## 35. Final A1 implementation allowlist

Luna may implement only:

```text
NativeSpy.Attach
NativeSpy.Attach.Windows
NativeSpy.Attach.Tests
NativeSpy.Attach.Windows.Tests

strict request/result DTOs and unions
ProcessIdentity attach comparer and validation
AttachId allocation and result correlation
composition ID validation
TargetRuntimeIdentity
architecture/runtime/composition/security support dispositions
ProbeOutcome without cancellation member
AttachTimeouts configuration
bounded diagnostic model
IAttachManager
IAttachStrategy
fake authenticated-client connector/session
fake strategy
AttachManager lifecycle
same-target operation gating
standalone and attach-owned probe distinction
identity-consistent read-only Windows probe
mandatory pre-entry identity reread seam
manager-owned cleanup lifetime
Settling/CleanupUnknown transitions
shared detach task semantics
session ownership/disposal rules
manager IAsyncDisposable semantics
foreign-manager contract checks
stale-result guards
strict connector request/result/projection fakes
client-session completion/termination fakes
closed adapter contract and winforms.correlation mapping
structured runtime-version values
finite timeout validation
canonical lower-layer failure mapping
PostFailureLifecycle mapping
deterministic tests
```

A1 may add only the narrow connector interface/adaptation required by the frozen `NativeSpy.Client` seam. It must not redesign `NamedPipeClientSession` or I3.

A1 must not implement a real target lifecycle rendezvous or resident coordinator.

---

## 36. Final A1 prohibited list

A1 must not implement:

```text
remote injection
native bootstrap
remote memory/thread work
real lifecycle rendezvous
GetNamedPipeClientProcessId production rendezvous integration
LifecycleOwnerProof target storage/validation
resident process coordinator
AgentHost StopAsync change
Agent TFM changes
runtime module-name/version compatibility heuristics
payload-layout heuristics
single-file/self-contained heuristics
payload compatibility rules
arbitrary WinForms execution-context discovery
ObjectSpy/FlaUI integration
WPF
writes/invocation
reconnect
multi-client support
leases
automatic elevation
service/broker/debugger/profiler architecture
```

A1 must not claim that a real arbitrary Windows process is attachable.

---

## 37. Contradiction audit and A0 acceptance

The frozen design was audited against:

```text
dependency graph vs runtime call graph
public API vs lifecycle ownership
secret custody and destruction
ProcessIdentity equality and reread
AttachId allocation ordering
same-target Probing
neutral/composition probe eligibility
security SID/integrity policy
I3 connector placement
lifecycle rendezvous delivery
LifecycleOwnerProof semantics
orphan reconciliation entry
caller timeout/cancellation precedence
cleanup ownership
Settling/CleanupUnknown transitions
Dispose/Detach races
foreign-manager behavior
bounded diagnostics
strict result unions
new project TFMs/platform targets
current AgentHost behavior vs future B1 behavior
```

A1-relevant decisions are concrete and closed. Later B0/C0/D0/E0/F/G choices are explicitly labeled and are not A1 decisions.

### 37.1 Final corrective finding map

```text
F-01  pre-entry ProcessIdentity revalidation                         §5.4
F-02  peer-PID primitive ownership and fail-closed policy             §23.3
F-03  initial accepted composition set                                §§6.3, 6.6, 9.11
F-04  concrete authenticated connector handoff                         §10.1
F-05  authenticated client-session projection and Completion           §10.2–10.4
F-06  bounded adapter contract and terminal behavior                   §9.7
F-07  independent ObjectSpy/Attach generation guards                   §24.4–24.5
F-08  conservative architecture/runtime evidence policy                §7.2–7.3
F-09  structured runtime-version and diagnostic bounds                 §§7.1, 18
F-10  finite timeout validation                                         §16.1
F-11  typed connector failures                                           §10.2
F-12  normalized outer failure taxonomy                                  §17.1–17.3
F-13  exact failure-to-state mapping and PostFailureLifecycle            §17.5
```

The later corrective sections additionally freeze detach/result linearization, manager disposal, pre-I3 disappearance, invalid-peer preemption, singular cleanup ownership, fresh-probe strategy gating, capability mapping, connector timeout enforcement, adapter terminality, and the expanded A1 tests.

---

## 38. Direct answers

1. **What identifies a target?** Exact `ProcessIdentityDto` with PID plus ordinal-equal process-start identity.
2. **How is equality implemented?** An explicit attach-layer comparer; DTO reference equality is never used.
3. **When is identity checked?** Before and after probe evidence, and again immediately before the first invasive strategy operation.
4. **When is AttachId allocated?** After valid request validation and before manager lifecycle inspection.
5. **Does a fail-fast attempt get an AttachId?** Yes, for every valid explicit AttachAsync invocation.
6. **Does invalid input get an AttachId?** No; it throws before allocation.
7. **Can standalone ProbeAsync reserve a target?** No.
8. **What happens when AttachAsync is already Probing?** A second same-target AttachAsync fails fast with `AttachInProgress`.
9. **What does neutral composition probing mean?** `CompositionSupport = NotRequested`; overall eligibility is `Unknown`.
10. **Which composition is initially supported?** Exactly `winforms`; other syntactically valid IDs are `KnownUnsupported` and return `UnsupportedComposition` before strategy invocation.
11. **Is TargetLayoutSupport public in A1?** No.
12. **How are composition IDs validated?** Lowercase ASCII, 1–64 characters, `[a-z0-9._-]`, no whitespace, ordinal comparison.
13. **How is cancellation represented?** `OperationCanceledException`, never a probe result member.
14. **Who owns cleanup after side effects?** Manager/coordinator-owned cleanup lifetime, not the caller token.
15. **When is state Settling?** A known shared cleanup/stop operation remains active.
16. **When is state CleanupUnknown?** Positive closure is unproven after shared cleanup loses a trustworthy completion path or exceeds CleanupSettlement.
17. **Can CleanupUnknown be force-reset?** No.
18. **Who derives AllowedUserSid?** Target-side startup derives it from the target primary token.
19. **What is the initial security policy?** Same primary SID and target integrity not higher than controller process integrity.
20. **Where is the I3 connector seam?** `NativeSpy.Client`, using the frozen `AuthenticatedClientConnectRequest`, strict result union, and session projection.
21. **Does the projection expose terminal completion?** Yes, one exactly-once bounded `Completion` signal.
22. **Does AttachSession expose NamedPipeClientSession or secrets?** No.
23. **How is lifecycle detach delivered?** Strategy-driven re-entry into the resident coordinator through a fresh one-use lifecycle rendezvous.
24. **How is live detach authorized?** Exact AttachId, exact ProcessIdentity, fresh rendezvous token, and LifecycleOwnerProof.
25. **How is orphan reconciliation authorized?** Only after independent client-loss evidence, through fresh authenticated current-orphan reconciliation.
26. **Can a foreign manager stop a live session?** No.
27. **What happens if a foreign manager passes another manager's session?** Ordinary contract/state exception.
28. **What does DisposeAsync do?** Local idempotent cleanup only; it never claims target detach.
29. **What happens when Dispose races Detach?** A synchronized transition claims either `Active → DetachClaimed` or `Active → LocallyDisposed`; the loser cannot replace authoritative lifecycle results.
30. **What happens after manager disposal?** New public operations throw `ObjectDisposedException`; existing side-effecting cleanup remains manager-owned; disposal never claims detach.
31. **What happens after detach?** The target remains running; old identities are terminal; a later explicit attach creates fresh identities.
32. **What may A1 implement?** Contracts, comparer, AttachId ordering, pre-entry identity guard seam, probe/security semantics, fake manager/strategy/connector/session/adapter, deterministic lifecycle tests, and read-only Windows probing only.

---

**End of frozen I5-A0 architecture document.**
