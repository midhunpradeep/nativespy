# NativeSpy I5-A0 — Attach Contract, Lifecycle, Scope, and Failure Model

**Starting architecture-document revision:** `eb34374261a3a00ae8e2245c18ce867af852b11b`
**Production-source baseline:** `c02fbbceb7a389d806860bfe7d6e01d7f20cf7e9`
**Status:** ready for independent closure review.
**Scope:** architecture documentation only. I5-A1 has not been implemented.

The production-source baseline is the authority for repository facts. Architecture-document commits after that baseline do not make proposed behavior repository behavior.

## Evidence labels and normative language

- **REPOSITORY FACT** — verified in source/tests/project references at the production baseline.
- **FROZEN PRODUCT DECISION** — fixed product behavior in this specification.
- **WINDOWS/.NET DOCUMENTED FACT** — supported by linked current Microsoft documentation.
- **ARCHITECTURAL DECISION** — necessary design to satisfy product decisions and repository constraints.
- **FUTURE DECISION** — explicitly owned by a later named iteration; not an A1 decision.

“MUST”, “MUST NOT”, and “ONLY” are normative. Private helper names, collection types, method decomposition, and synchronization primitives are open only where changing them cannot change specified semantics.

---

## 1. Product goal and initial scope

NativeSpy pairs two identity domains without merging them:

```text
FlaUI / UI Automation identity
CLR object identity
correlation evidence between them
```

Only `CorrelationStatus.Exact` may establish a CLR root for a UIA selection. Weaker evidence remains UIA/correlation diagnostics and MUST NOT guess CLR identity. Attach does not change this rule. Dynamic inspection is the I5 substrate; generated bindings, writes, method invocation, collection browsing, and a full Object Browser are not I5-A1 work.

I5 makes attach possible to an already-running application that did not start NativeSpy. It covers exact process identity, support and security evidence, entry, one active NativeSpy generation, bootstrap handoff, I3 session establishment, detach, crash recovery, reattach, and eventually arbitrary WinForms execution-context discovery.

The first eventual real attach slice is exactly:

```text
OS                 Windows on AMD64
NativeSpy          x64
target process     x64
attach mode        already-running target only
runtime            .NET 10 CoreCLR runtime major band
composition        WinForms
security           same primary user SID and same integrity level
```

The runtime band includes supported .NET 10 servicing versions; it is not one exact patch, an application TFM string, filename convention, command line, environment, or build-configuration inference. Runtime identity, payload compatibility, and process-entry compatibility are distinct facts (§20).

Initial exclusions: ARM64 Windows (including x64-emulated targets), x86, ARM64 targets, .NET Framework, WPF target support, native/AOT targets, launch-time attach, startup hooks, UIA2, multi-client sessions, transparent reconnect, leases, automatic elevation, privileged broker/service, writes, invocation, collection browsing, and generated bindings. Process-picker UX and enumeration belong above `NativeSpy.Attach`; it accepts an already-selected exact process identity.

I5-A1 implements semantic contracts, manager lifecycle, fakes, deterministic tests, read-only Windows probing, and the first-entry gate. A1 does not attach an arbitrary real target. B/C/E provide real bootstrap, native entry, and arbitrary WinForms behavior.

---

## 2. Repository facts at the production baseline

### 2.1 Projects and dependency facts

**REPOSITORY FACT:** relevant existing projects and target frameworks are:

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

The existing project-reference graph is acyclic. No attach projects exist at the source baseline. Protocol, Agent, and Client do not depend on a native injector.

### 2.2 Process identity

`ProcessIdentityDto` is a sealed class with `ProcessId` and `ProcessStartIdentity`. It validates positive PID and nonblank start identity but has no value equality. `ProcessIdentityReader` lives in `NativeSpy.Transport.NamedPipes`; it opens a PID-based process handle, reads process creation time, and serializes the unsigned FILETIME as invariant-culture decimal text.

Attach code MUST use an explicit value comparer, never reference equality. A1 accepts nonblank, well-formed Unicode start-identity text bounded to 256 UTF-8 bytes so untrusted requests cannot cause unbounded gate-name work. It does not require a new lexical format or alter reader-produced values. Hash encoding uses strict UTF-8, exact stored text, and no normalization. Exact equality is PID equality plus ordinal `ProcessStartIdentity` equality (§4).

### 2.3 AgentHost, I3, WinForms, and ObjectSpy

**REPOSITORY FACT:**

- `AgentHostOptions.CreateDefault(targetIdentity, allowedUserSid)` currently creates the pipe name and bootstrap nonce. Its caller invokes the factory. `AgentHost` consumes options and creates the descriptor. Current WinForms bootstrap accepts caller-provided SID material.
- Current I3 validates descriptor kind, exact target identity, bootstrap nonce, SID, and protocol range before returning a fresh Agent `SessionId`. `NamedPipeClientSession` implements both `IClrInspectionPort` and `IWinFormsCorrelationPort`.
- `NamedPipeClientSession.ConnectAsync(BootstrapDescriptorWire, ...)` applies `ConnectTimeout` to pipe connection, but not the frozen end-to-end connect-plus-Hello stage budget. The attach adapter MUST enforce the complete supplied I3 timeout (§8.3).
- `NamedPipeConnection` can observe connected user SID server-side but does not expose peer PID. A production peer-PID primitive is B1 work.
- `AgentHost.StopAsync` is state-idempotent but not task-idempotent: concurrent callers can run shutdown work. Current shutdown can swallow disposal failures while reaching `Closed`; state alone is not proof of closure. B1 MUST make stop callers share one logical task and provide truthful closure evidence.
- After some failed pre-handshake connections, AgentHost can return to listening. This is not permission to admit a second generation; attach-owned failure cleanup requests the shared stop.
- `WinFormsHostCompositionFactory(Control)` and `WinFormsUiDispatcher(Control)` require a fixed caller-provided Control. Current code does not discover arbitrary app contexts, multiple UI threads, or HWND-to-context routing.
- The WinForms composition advertises adapter ID `winforms`; the existing client correlation surface is `IWinFormsCorrelationPort`.
- `FlaUiAutomationSession.Attach(ProcessIdentityDto)` reduces identity to PID. `ObjectSpyCoordinator` checks UI session PID against the requested PID, not exact incarnation. Later integrated ObjectSpy work MUST add an exact-incarnation seam.
- Current frozen FlaUI selection does not deterministically release its live UIA source before its session ends. Revisit when I5/ObjectSpy longer-lived session use requires it.

These are baseline observations, not claims that future attach behavior exists.

### 2.4 Preserved I0–I4a invariants

I5 preserves without reinterpretation:

```text
Exact-only CLR identity exposure; UIA and CLR identities never merge
weak/session-scoped CLR handles; no reacquisition; runtime-boundary identity
strict JSON/protocol and existing I3 authentication/negotiation
Host-authoritative target timeout; client abandonment remains client-side
no forced getter/callback abortion; old target code may finish naturally
Client owns correlation certainty; live UIA stays in FlaUI; live CLR stays in Agent
no hidden leases, multi-client session, or transparent reconnect
```

Attach does not weaken these rules. Session loss is terminal; explicit reattach creates fresh identities.

---

## 3. Project ownership and dependency direction

### 3.1 Project split

```text
NativeSpy.Attach
    framework-neutral requests/results and contracts
    AttachManager and public session lifecycle
    strategy/connector seams, failures, support, timeouts

NativeSpy.Attach.Windows
    read-only Windows process/runtime/security probe
    Windows first-entry gate and private validated-target seam
    later rendezvous and native process entry

NativeSpy.Transport.NamedPipes
    low-level named-pipe transport
    existing ProcessIdentityReader
    later low-level connected peer PID observation only

NativeSpy.Client
    IClrInspectionPort, IWinFormsCorrelationPort semantics, and adapter base
    closed client-side adapter wrapper
    internal authenticated-session connector seam

NativeSpy.Client.NamedPipes
    NamedPipeClientSession/I3 connector adapter
    B1 rendezvous transport adapter using the Transport peer-PID primitive

NativeSpy.Agent / NativeSpy.Agent.WinForms
    target-side CLR and framework evidence

NativeSpy.ObjectSpy
    orchestration/pairing of FlaUI and AttachSession
```

`NativeSpy.Attach` targets `net10.0` and contains no Windows APIs or WinForms types. `NativeSpy.Attach.Windows` targets `net10.0-windows`, x64 for the initial slice. `NativeSpy.Attach.Tests` targets `net10.0`; Windows tests target `net10.0-windows`, x64. Do not create another abstraction project without a concrete dependency constraint.

### 3.2 Dependency graph

```text
NativeSpy.Protocol
    ↑
NativeSpy.Client
    ↑
NativeSpy.Attach
    ↑
NativeSpy.Attach.Windows

NativeSpy.Transport.NamedPipes → NativeSpy.Protocol
NativeSpy.Client.NamedPipes → NativeSpy.Client + Protocol.Json + Transport.NamedPipes
NativeSpy.Attach.Windows → NativeSpy.Attach + Transport.NamedPipes
NativeSpy.ObjectSpy → NativeSpy.Attach + NativeSpy.Client + NativeSpy.FlaUI
NativeSpy.ObjectSpy.App → ObjectSpy + Attach.Windows + Client.NamedPipes
```

`NativeSpy.Attach` may reference `NativeSpy.Client` for the CLR port, generic adapter contract, and internal connector seam. The connector seam belongs in Client to avoid a Client→Attach cycle; it is internal to composition and exposed only to necessary friend assemblies. Bootstrap data on that seam is not an ObjectSpy-facing contract.

`NativeSpy.Attach.Windows` does not own WinForms semantics. `NativeSpy.Client` owns the detached client-side WinForms adapter; `NativeSpy.Agent.WinForms` owns target-side WinForms evidence; ObjectSpy orchestrates both. Protocol, Agent, Client, and Transport MUST NOT reference native injector implementation.

---

## 4. Exact process identity and AttachId

### 4.1 Exact identity

Every public `TargetProbeRequest`, `AttachRequest`, `AttachSession`, and detach operation names the exact `ProcessIdentityDto`. PID alone is never a valid target identity, probe authorization, or attach authorization. There is no PID-only public attach overload and no silent retargeting.

A1 validates positive PID and bounded well-formed `ProcessStartIdentity` text as described in §2.2. It does not alter values produced by `ProcessIdentityReader`. Equality is:

```text
left.ProcessId == right.ProcessId
AND string.Equals(left.ProcessStartIdentity,
                  right.ProcessStartIdentity,
                  StringComparison.Ordinal)
```

All maps, gates, lifecycle comparisons, probe checks, rendezvous checks, and stale-result guards use this comparer.

### 4.2 Revalidation and same-process-object binding

A read-only probe checks exact identity before and after evidence collection. Any evidence collected across mismatch is discarded. At first invasive process entry, the Windows strategy opens the target with rights required for the next operation, reads creation identity from that same process object, and compares it with the request. Only this validated object can be used for invasive calls; the strategy MUST NOT reopen by PID as authorization.

Private/internal conceptual seam:

```text
IValidatedTargetProcess
    ProcessIdentity
    strategy-private operations bound to this process object
    disposal
```

It exposes no public raw HANDLE, address, memory operation, or native-entry method. A1 defines the seam and fake tests; C0 owns concrete process-handle rights; C1 proves real same-handle behavior. A1 reuses existing identity semantics and does not duplicate `ProcessIdentityReader`.

```text
no process at first observation                    → TargetNotFound
known requested incarnation positively exited     → TargetExited
PID now names another incarnation                  → TargetIdentityChanged
identity evidence access denied                    → AccessDenied
identity cannot be established                     → fail closed; no entry
```

A restart between selection/probe, probe/attach, bootstrap/handshake, or any other stage never becomes the selected process merely because PID matches.

### 4.3 AttachId

Every syntactically valid explicit `AttachAsync` call receives a fresh opaque `AttachId`, including fail-fast, unsupported, cancelled, and failed attempts. Validate request first; invalid programmer input gets no ID. Allocate AttachId before inspecting manager lifecycle state.

AttachId is random, opaque, non-reused, and only a correlation/generation identity. It is not a secret, authorization credential, capability token, or substitute for ProcessIdentity, Agent `SessionId`, HandleId/MemberId/TypeId, or ObjectSpy generations. Authentication uses separate secret material (§12). A successful session retains its AttachId for its lifetime; explicit reattach gets a new one.

---

## 5. Probe contract and support model

### 5.1 Request and behavior

```text
TargetProbeRequest
    TargetProcessIdentity       required exact identity
    RequestedCompositionId      null or one exact opaque ID
```

Null means neutral probe. Omission never means “all compositions supported” or “eligible.”

`ProbeAsync` is read-only with respect to the target, bounded, cancellable, non-reserving, and point-in-time. It may run while any lifecycle state exists. A positive exact-target exit observation may terminalize the manager's local record as `TargetExited`; other support/lifecycle observations do not reserve or change target lifecycle. It does not authorize a later attach. A stale result is UI evidence only; `AttachRequest` cannot contain a prior probe result. Process discovery/picker UX is above this API.

### 5.2 Independent result dimensions

A completed probe keeps separate:

```text
TargetArchitecture / ArchitectureSupport
TargetRuntimeIdentity / RuntimeSupport
RequestedCompositionId / CompositionSupport
SecuritySupport
OverallAttachEligibility
ExistingNativeSpyState (advisory only)
```

Support values are `KnownSupported`, `KnownUnsupported`, `Unknown`; composition also has `NotRequested`. Overall eligibility is `Eligible`, `NotEligible`, or `Unknown`.

Neutral probe:

```text
CompositionSupport = NotRequested
OverallAttachEligibility = Unknown
```

A composition-specific result can be `Eligible` only when architecture, runtime, exact requested composition, and security are all `KnownSupported`. Any unsupported required dimension gives `NotEligible`; any unresolved required dimension gives `Unknown`. Runtime support never implies composition support or vice versa.

`TargetProbeResult` contains the requested identity, optional observed identity, `ProbeOutcome`, architecture/support, optional runtime identity/support, requested composition/support, security support, overall eligibility, advisory NativeSpy state, bounded fixed-enum limitations, and bounded diagnostic. A non-completed outcome has no eligible support result. No active AttachId or secret is included.

### 5.3 Probe outcomes and cancellation

`ProbeOutcome` is exactly one of:

```text
ProbeCompleted
TargetNotFound
TargetExited
TargetIdentityChanged
AccessDenied
TimedOut
InternalFailure
```

Caller cancellation throws `OperationCanceledException`, not a result value. Standalone deadline expiry returns `TimedOut`. A non-completed result cannot be eligible. If this manager/probe has positive evidence the requested incarnation exited, report `TargetExited`; if the process was never observed, absence may be `TargetNotFound`. A reused PID with a different start identity is never accepted and is `TargetIdentityChanged` or `TargetExited` according to evidence.

### 5.4 Fresh attach-owned probe and fail-fast ordering

Every valid `AttachAsync` allocates a fresh `AttachId` before the authoritative manager-local lifecycle check. Every attempt that proceeds to target eligibility evaluation or target-entry consideration MUST obtain a fresh composition-specific probe for that attempt. A caller-provided or stale standalone probe never authorizes entry.

Authoritative local fast paths are:

```text
Attached                         → AlreadyAttached; no probe
Probing/Bootstrapping/Connecting → AttachInProgress; no probe
Detaching/Settling               → AttachInProgress; no probe
TargetExited                     → TargetExited; no strategy
CleanupUnknown                   → exactly one bounded ReconcileAsync first
Detached                         → fresh composition-specific probe
```

For a locally known `CleanupUnknown`, request validation and fresh `AttachId` allocation happen first, followed by exactly one bounded reconciliation and no Host admission. If it proves closure, lifecycle becomes `Detached` and the same attempt performs a fresh composition-specific probe. If the target exited, return `TargetExited`. If unresolved or timed out, return `CleanupUnknown`; do not call `BeginAttachAsync`.

On a `Detached` path, perform the fresh composition-specific probe. A non-completed probe returns its typed probe failure without strategy invocation. `SecuritySupport = KnownUnsupported` returns `UnsupportedSecurityContext / Probe`; security evidence denied by Windows returns `AccessDenied / Probe`; inconclusive security evidence returns `ProbeInconclusive / Probe`. All three have `PostFailureLifecycle = Detached` and invoke no strategy. Only after `SecuritySupport = KnownSupported` may the attempt invoke reconciliation or entry strategy. A completed probe that observes an orphan-reconcilable coordinator condition (`Orphaned`, or `Settling`/`CleanupUnknown` for a generation whose authenticated owner is independently known absent) MUST trigger exactly one bounded `ReconcileAsync`; this is mandatory, not optional. The coordinator independently validates current generation and owner authorization. The outcome map below is specifically for this mandatory post-probe advisory-orphan reconciliation; the earlier manager-local `CleanupUnknown` fast path retains its separate pre-probe mapping. If the generation changed since the advisory probe, classify the result using the coordinator's current authoritative state, never the stale observation. Its outcomes are:

```text
Detached proven
    → target lifecycle becomes Detached; perform a NEW fresh
      composition-specific probe; continue this same AttachAsync only if
      the new probe is Eligible; no AttachFailure yet
TargetExited proven
    → TargetExited / Reconciliation, PostFailureLifecycle = TargetExited;
      old exact ProcessIdentity is terminal; no BeginAttachAsync
current state is live Attached
    → AlreadyAttached / Reconciliation, PostFailureLifecycle omitted;
      existing generation unchanged; no BeginAttachAsync
current state is a non-reconcilable transition (including
Bootstrapping/Connecting/Detaching/Settling)
    → AttachInProgress / Reconciliation, PostFailureLifecycle omitted;
      existing generation unchanged; no BeginAttachAsync
reconciliation completes without proving Detached or TargetExited
    → CleanupUnknown / Reconciliation,
      PostFailureLifecycle = CleanupUnknown; no BeginAttachAsync, target remains
      blocked, no second Host
manager-owned Reconciliation budget expires before a definitive result
    → StageTimedOut / Reconciliation,
      PostFailureLifecycle = CleanupUnknown; target lifecycle becomes
      CleanupUnknown; preserve timeout as cause; no BeginAttachAsync, no second
      Host, no background polling
```

A coordinator's authoritative lifecycle rejection is a normal lifecycle conflict, not a rendezvous authentication failure. Transport, authentication, and peer-verification failures retain their existing rendezvous failure codes and `AttachStage = Rendezvous`; they are never translated to `AlreadyAttached` or `AttachInProgress`. A stale advisory observation never authorizes a stop. A live authenticated session is not orphan-reconcilable; the coordinator returns its authoritative conflict without stopping it.

For any attempt that completes its required probe/reconciliation sequence:

```text
probe not completed                 → typed probe failure; no BeginAttachAsync
eligibility != Eligible             → corresponding typed support failure; no BeginAttachAsync
eligibility == Eligible             → BeginAttachAsync at most once
```

A valid unsupported composition is probed and produces `KnownUnsupported`. `BeginAttachAsync` independently checks the coordinator gate before Host admission and returns the current typed lifecycle conflict rather than starting a second Host. These pre-Host coordinator conflicts use `AttachStage = LifecycleAdmission` and omit `PostFailureLifecycle`; reconciliation conflicts use `AttachStage = Reconciliation` and likewise omit it. There is no retry loop. Per valid attempt, `ReconcileAsync` and `BeginAttachAsync` each run at most once; normal Detached attach uses zero reconciliations, while CleanupUnknown/orphan recovery uses one. Reconciliation is lifecycle work, not target entry. Unknown/Unsupported eligibility never permits `BeginAttachAsync`.

### 5.5 Advisory NativeSpy state

Optional lifecycle observation is:

```text
NotObserved | Detached | Bootstrapping | Attached | Detaching |
Settling | Orphaned | CleanupUnknown | Unknown
```

`NotObserved != Detached`. Observation is not authorization. Ordinary probe never exposes a live AttachId, owner proof, endpoint, or takeover capability.

---

## 6. Windows architecture, runtime, and security evidence

### 6.1 Architecture

A1 uses documented `IsWow64Process2` process/native machine evidence for both the controller and target. When `pProcessMachine` is `IMAGE_FILE_MACHINE_UNKNOWN`, the process is native and its machine is `pNativeMachine`; otherwise use `pProcessMachine` as the process machine. Initial support requires all of:

```text
NativeSpy process machine = AMD64
Target process machine    = AMD64
Windows native machine    = AMD64
```

An x64 process on ARM64 Windows is `KnownUnsupported`, including x64 emulation; an x64 process-machine value alone is insufficient. Positively identified x86, ARM64, or other architectures are `KnownUnsupported`; incomplete/unrecognized evidence is `Unknown`. Do not infer from executable name. The API requires query process rights; failures are classified from actual evidence. Read-only probing requests no process-entry/write rights. [Microsoft: IsWow64Process2](https://learn.microsoft.com/en-us/windows/win32/api/wow64apiset/nf-wow64apiset-iswow64process2)

### 6.2 Runtime identity and support

`TargetRuntimeIdentity` describes bounded observed runtime evidence only:

```text
RuntimeFamily: CoreCLR | FrameworkClr | NativeOrNone | Unknown
Version:       bounded numeric Major/Minor/Build/Revision when verified
VersionEvidence: Exact | MajorMinor | MajorOnly | Unknown
```

`NativeOrNone` means no managed-runtime evidence was observed within the bounded probe; it does not prove that the application is intrinsically native. These fields do not assert TFM, deployment model, single-file layout, self-contained compatibility, payload compatibility, or native-entry compatibility.

A1 collects bounded, read-only Windows loaded-module/process evidence using documented APIs. It MUST NOT infer runtime identity/version from command line, environment, executable name, application TFM, a coarse framework label, or an arbitrary target-controlled module basename. No target memory is written or used for arbitrary diagnostics. A1 MUST NOT invent a module-authentication algorithm or weaken evidence to produce a supported result.

Keep `RuntimeFamily`, `RuntimeVersionEvidence`, and `RuntimeSupport` separate. `RuntimeSupport = KnownSupported` is justified only when the observed loaded-module evidence reliably binds the target's runtime identity and version to the initial .NET 10 CoreCLR band. `KnownUnsupported` is justified only when trustworthy evidence positively establishes an incompatible runtime family/version. If the evidence cannot reliably bind the observed module to trustworthy runtime identity/version evidence, support is `Unknown`. Apply at least the same conservative standard to Framework CLR as to CoreCLR; if trustworthy evidence is not defined, return `Unknown` rather than inventing asymmetric rules. A real A1 probe may report `RuntimeFamily = CoreCLR` and apparent major version 10 while returning `RuntimeSupport = Unknown`. Fake A1 probes may return `KnownSupported` to exercise manager behavior. C0 owns the exact stronger evidence necessary to authorize the first real native-entry slice.

Runtime-support uncertainty is separate from payload-compatibility uncertainty. A .NET 10 runtime observation does not prove application TFM, deployment model, payload compatibility, or native-entry compatibility. The baseline Agent/Host assemblies target .NET 10; that repository fact does not establish the target application's TFM or payload compatibility. D0/D1+ owns broader runtime/payload compatibility. No invasive target execution is permitted for probing.

### 6.3 Controller/target SID and integrity

Controller primary user SID and integrity level come from its actual process primary token. Target values come from the target process primary token using Windows process/token APIs. Do not use caller-selected SID, thread impersonation token, environment value, or username string.

Initial support requires exact equality:

```text
controller primary user SID == target primary user SID
AND
controller integrity level == target integrity level
```

Different user or different integrity is `KnownUnsupported`, including same-user lower-integrity targets. A positively observed policy mismatch maps to `UnsupportedSecurityContext / Probe`, not `AccessDenied`. Missing required token evidence is `AccessDenied` if Windows denied access; otherwise it is `Unknown` and maps to `ProbeInconclusive / Probe`. Neither case invokes strategy from the Detached attach path. Matching SID alone is insufficient. Normal DACL access checks remain authoritative; there is no automatic elevation.

This equality rule is a conservative NativeSpy product choice, not a Windows requirement. MIC can deny lower-integrity writes despite DACL permission. A0/A1 adds no custom mandatory-integrity pipe-label policy. [Microsoft: Mandatory Integrity Control](https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control), [Process Security and Access Rights](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights), [OpenProcessToken](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocesstoken), [GetTokenInformation](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-gettokeninformation).

Rendezvous ACLs derive from actual controller identity. Target AgentHost `AllowedUserSid` derives from the target's own primary token. Controller input MUST NOT choose the target Agent pipe ACL SID. The current repository does not yet satisfy this target-side ownership; B0 freezes the target bootstrap API and B1 implements it.

### 6.4 Composition

`AttachRequest` contains exactly one `RequestedCompositionId`. A1 recognizes only `winforms`. IDs are 1–64 ASCII lowercase characters from `[a-z0-9._-]`, ordinal comparison. Invalid lexical input is programmer-contract misuse. A valid unsupported ID is a domain request and probes `KnownUnsupported`; no fallback is allowed.

Neutral probe means `NotRequested`, not support. Runtime support cannot establish WinForms support. At the source baseline there is no safe repository mechanism that proves an arbitrary target has a usable WinForms composition before entry. Therefore A1's real Windows probe reports `CompositionSupport = Unknown` for `winforms` unless a separately defined trustworthy evidence source proves the exact requested composition; it MUST NOT infer support from runtime, application TFM, executable/module basename alone, HWND presence, or process name. A1 fakes may return supported evidence for manager tests. Before E0, B1/C1 integration may inject internal test-only `KnownSupported` evidence for a controlled cooperative target; there is no public override and this is not production support discovery. E0 must define pre-entry evidence that can produce production `KnownSupported`; E1 must prove the arbitrary application path. Until then real arbitrary WinForms attach remains ineligible, not best-effort.

A successful initial `winforms` attach semantically requires:

```text
authenticated I3 session
IClrInspectionPort
registered adapter ID "winforms"
```

The authenticated I3 capabilities must include adapter ID `winforms` and the existing WinForms operations `correlation.winforms.beginCurrentHwnd` and `correlation.winforms.revalidateCurrentHwnd`, plus the CLR operations required by the existing inspection port: `clr.describeObject`, `clr.listMembers`, `clr.readFieldValues`, and `clr.readPropertyValue`. The concrete outer client composition registers `winforms` only for an adapter implementing the existing `IWinFormsCorrelationPort`. The framework-neutral Attach project validates exact opaque IDs/required operation names and the closed adapter registration; it MUST NOT reference a WinForms type. If any required surface is unavailable, attach fails, cleanup is tracked, and no partial AttachSession is published. There is no generic-CLR downgrade.

---

## 7. Public attach contracts and AttachSession

### 7.1 Manager and request/result

The sole public lifecycle owner is `IAttachManager`:

```text
ProbeAsync(TargetProbeRequest, CancellationToken) → TargetProbeResult
AttachAsync(AttachRequest, CancellationToken) → AttachResult
DetachAsync(AttachSession, CancellationToken) → DetachResult
DisposeAsync() (IAttachManager extends IAsyncDisposable)
```

`AttachRequest` contains exact `TargetProcessIdentity` and one exact `RequestedCompositionId`. No prior probe result, PID-only target, process handle, HWND, target path, command line, payload path, remote address, token, or framework object appears in it.

`AttachResult` is a strict union:

```text
Success(AttachId, AttachSession)
Failure(AttachId, AttachFailure)
```

`AttachFailure` contains exact target identity, stage, stable failure code, bounded diagnostic, optional `PostFailureLifecycle`, and an optional cleanup-failure code while preserving the original causal failure. `PostFailureLifecycle` is required when the failed attach attempt itself entered or caused a target lifecycle/cleanup state. Probe/support failures continue to report `Detached`, and target-exit outcomes report `TargetExited`. It is omitted for authoritative lifecycle conflicts where the attempted attach caused no target-side lifecycle transition, including `AlreadyAttached` and `AttachInProgress`, whether discovered during initial `LifecycleAdmission`, the pre-Host coordinator admission check in `BeginAttachAsync`, or mandatory `Reconciliation`. Its present values are `Detached`, `Settling`, `CleanupUnknown`, or `TargetExited`. `AttachResult` contains exactly one of `Success` or `Failure`. Every valid request has the fresh AttachId allocated under §4.3. Invalid caller input throws before ID allocation.

### 7.2 AttachSession public surface

A successful generic session exposes only:

```text
AttachId
exact TargetProcessIdentity
TargetRuntimeIdentity
RequestedCompositionId
AgentSessionId
IClrInspectionPort
immutable EffectiveCapabilities
read-only AttachSessionState
small typed adapter lookup
IAsyncDisposable
```

It MUST NOT expose `BootstrapDescriptor`, pipe name, nonce, rendezvous endpoint/token, LifecycleOwnerProof, `NamedPipeClientSession`, raw HANDLE, remote address, native bootstrap detail, or Snoop concept.

AttachSession is bound to the creating AttachManager. Passing it to another manager is programmer/state misuse and throws; it is not a target-domain failure.

### 7.3 Session states and operational lifetime

Public state enum is exactly:

```text
Active
Detaching
LocallyDisposed
Detached
TargetExited
CleanupUnknown
```

Only `Active` accepts new CLR/adapter operations. Other states reject them with existing session-closed semantics; terminal metadata remains readable. Manager `Settling` during detach projects as `Detaching` while the session has not been locally disposed.

State is read-only and is a manager-owned projection, not an independent AttachSession state machine. Apply this deterministic precedence:

```text
positive target-exit evidence for this exact identity/generation → TargetExited
positive Host/session closure for this generation               → Detached
local Dispose claimed, with neither target fact above           → LocallyDisposed
manager detach/settlement in progress                            → Detaching
manager lifecycle CleanupUnknown                                → CleanupUnknown
otherwise                                                        → Active
```

`LocallyDisposed` is local-only and never asserts target closure. `CleanupUnknown` is not a positive terminal target fact and does not override a local-disposal projection; the manager still retains and reports its blocking lifecycle state. Later positive target-exit or detach-closure evidence updates the projection to `TargetExited` or `Detached`. A disposed session never becomes Active. Transition out of Active prevents new CLR/adapter calls; already-started target callbacks are not forcibly aborted and may finish naturally under the I0–I4a rules.

### 7.4 Adapter contract and lookup

The framework-neutral `IAttachAdapter` base contract is in `NativeSpy.Client`, alongside client correlation surfaces. It contains only `string AdapterId { get; }`. No metadata dictionary, arbitrary registry, reflection scan, dynamic activation, assembly discovery, or DI framework is allowed.

Session lookup is:

```text
bool TryGetAdapter<TAdapter>(
    string adapterId,
    out TAdapter? adapter)
    where TAdapter : class, IAttachAdapter
```

Rules:

```text
invalid lexical adapterId                 → ArgumentException
valid unknown adapterId                   → false
requested type does not match registration → false
session state is not Active               → false
```

Adapter IDs use the composition lexical form and ordinal comparison. Initial ID is exactly `winforms`. `NativeSpy.Client` owns the closed `WinFormsCorrelationAttachAdapter` wrapper implementing both `IAttachAdapter` and the existing `IWinFormsCorrelationPort`; it delegates to a client correlation port supplied by the connector. `NativeSpy.Client.NamedPipes` supplies the existing `NamedPipeClientSession` implementation after I3 and registers the wrapper under `winforms`. AttachManager checks the registered ID, not a WinForms type; the authenticated I3 `CapabilitiesWire.AdapterIds` projection, required `SupportedOperations`, and local typed registration must all satisfy §6.4. Absence of any required registration/capability yields `CapabilityMismatch` and no AttachSession. The outer ObjectSpy composition obtains that known adapter and supplies its correlation port to ObjectSpy. No AttachSession exposes `NamedPipeClientSession`. Previously obtained adapter references stay bound to the original authenticated session and reject operations after terminalization. They never migrate to a new AttachId.

`EffectiveCapabilities` is an immutable pair of sets projected from authenticated I3:

```text
SupportedOperations  exact Agent operation IDs
AgentAdapterIds      validated bounded projection of CapabilitiesWire.AdapterIds
```

`CapabilitiesWire.AdapterIds` maps 1:1 to `AgentAdapterIds`; this is a validated semantic projection, not a second capability namespace.

Each set has at most 64 entries. `SupportedOperations` use the existing protocol canonical operation-name grammar (ASCII letters/digits/underscore/dot, nonempty segments, maximum 128 characters, ordinal exact); `AgentAdapterIds` use 1–64 lowercase ASCII characters from `[a-z0-9._-]`, ordinal exact. No values, metadata dictionary, or target-supplied descriptions. Duplicate or malformed entries are malformed capability data and fail validation; they are not silently deduplicated. The separate client-side `IAttachAdapter` registry remains bounded and does not masquerade as Agent capabilities.

### 7.5 Detach result

`DetachResult` is a strict union carrying exact AttachId, ProcessIdentity, one outcome, and bounded diagnostic/failure data:

```text
Authoritative: Detached | TargetExited | CleanupUnknown
Non-authoritative caller wait: DetachFailed + CallerWaitOnly + Settling + typed caller-wait-timeout
Operation failure: DetachFailed + SharedOperation + current Settling/CleanupUnknown lifecycle
```

Authoritative lifecycle outcomes are `Detached`, `TargetExited`, and the current `CleanupUnknown` state. `DetachFailed` is a typed operation failure, not a terminal lifecycle state. Its lifecycle remains `Detaching`/`Settling` or `CleanupUnknown`.

A per-caller wait timeout returns the non-authoritative `Settling` observation; it is not cached as target truth. Caller cancellation throws `OperationCanceledException` and returns no result. For a known manager-owned session, repeated DetachAsync joins the active shared operation or returns its current result without repeating target stop work. If the shared operation ends without closure proof, cache its `DetachFailed`/current-lifecycle observation for repeats; never issue a second stop for that AttachId. `CleanupUnknown` is cached as current lifecycle truth; if authorized reconciliation later proves `Detached`, the cached result for that same AttachId becomes `Detached`. No contradictory success/failure union is constructible.

---

## 8. Strategy and authenticated I3 connector seams

### 8.1 IAttachStrategy

`IAttachStrategy` is an internal mechanism seam, not a second lifecycle manager. Its exact semantic operations are:

```text
BeginAttachAsync(StartRequest, managerOperationToken) → StartResult
DetachAsync(DetachRequest, managerOperationToken)    → LifecycleResult
ReconcileAsync(ReconcileRequest, managerOperationToken) → LifecycleResult
```

Only AttachManager calls them. The public API has no `ReconcileAsync`.

AttachManager owns AttachId allocation, lifecycle state/public results, same-target serialization, fresh probe, caller cancellation/publication decision, connector use, shared detach task, and stale-result rejection. Strategy owns only OS/mechanism steps required to advance the manager-requested stage. It cannot allocate a session generation, retry, publish AttachSession, start a second Host, or expose raw handles, addresses, remote threads, DLL exports, or wire command strings.

Start request carries AttachId, exact ProcessIdentity, requested composition, relevant explicit stage budgets, and a private manager operation identity used only for stale-result checks. It does not carry caller cancellation as sole cleanup authority. The manager gives the advancing start/connector stage an operation token that it cancels on caller abandonment or stage expiry, separate from the cleanup/settlement lifetime token. A strategy MUST stop advancing toward successful publication when that token is cancelled and return a truthful typed state within its bounded stage. Cleanup uses the independent manager-owned lifetime. Start result is a strict union:

```text
ReadyForClientConnection:
    internal authenticated-connector request
    private LifecycleOwnerProof
    exact ProcessIdentity + AttachId
Conflict:
    authoritative current lifecycle Attached | TransitionInProgress
    no target generation/lifecycle transition caused by this attach attempt
Failure:
    typed stage/code + PostFailureLifecycle when required by §7.1
```

Any side-effecting failure must truthfully report `Detached`, `Settling`, `CleanupUnknown`, or `TargetExited`. Strategy-internal lifecycle results carry exact identity and generation correlation.

Before admitting a new Host/session, `BeginAttachAsync` asks the resident coordinator for its current lifecycle under the target gate. Authoritative mapping is:

```text
Attached                         → AlreadyAttached
Bootstrapping/Connecting/
Detaching/Settling                → AttachInProgress
CleanupUnknown                   → CleanupUnknown
Target exited                    → TargetExited
Detached                         → may admit this requested generation
Orphaned                         → never admit a Host; if discovered by this
                                   attempt's fresh probe, reconcile exactly once
                                   before BeginAttachAsync (§5.4)
```

The strategy MUST NOT start a second Host on any other state. Lifecycle results are a strict union of `Detached`, `TargetExited`, `Settling`, `CleanupUnknown`, `Conflict(Attached | TransitionInProgress)`, or typed stage failure with post-failure lifecycle. Every result carries exact ProcessIdentity and the target-generation AttachId when one exists. AttachManager maps `Conflict(Attached)` to `AlreadyAttached`, `Conflict(TransitionInProgress)` to `AttachInProgress`, and never treats a conflict as success. A conflict returned by the pre-Host `BeginAttachAsync` coordinator check is `LifecycleAdmission`; a conflict returned by mandatory orphan `ReconcileAsync` is `Reconciliation`.

`DetachAsync` receives exact ProcessIdentity, exact target-generation AttachId, private owner proof, and manager deadlines. Reconciliation carries the current attach-attempt correlation plus an expected old generation when known; it can otherwise bind only through the coordinator's atomic current-orphan operation. It never means “stop whatever is active.” A live owner session is not reconcilable by a foreign manager. No strategy contract contains native implementation detail.

### 8.2 Authenticated client-session connector

The transport-neutral `IAuthenticatedClientSessionConnector` seam is internal to `NativeSpy.Client` and implemented by composition in `NativeSpy.Client.NamedPipes`. A1 uses a fake connector. Any production adapter change in A1 is limited to the narrow wrapper required by this contract; it MUST NOT redesign I3.

Its request contains exactly the semantic connection data needed by existing I3:

```text
ExpectedTargetProcessIdentity
Agent endpoint
BootstrapAuthenticationSecret (existing bootstrap nonce)
SupportedProtocolRange
I3HandshakeTimeout (one finite TimeSpan)
```

The request and descriptor remain attach/session-establishment internals, never AttachSession properties. The connector result is a strict success/failure union. Success projects:

```text
AgentSessionId
IClrInspectionPort
bounded capabilities and adapter lookup
Completion (exactly-once terminal signal)
local asynchronous disposal
```

Completion distinguishes `RemoteClosed`, `LocalDisposed`, `ProtocolFailure`, and `TransportFailure`; no arbitrary exception object crosses the contract. Connector failure values preserve exact lower-layer `ProtocolErrorCode` or `OperationErrorCode` when applicable.

### 8.3 I3 timeout and preservation

`AuthenticatedClientConnectRequest` MUST carry explicit `I3HandshakeTimeout`. The connector enforces that manager-owned deadline across the complete pipe-connect + Hello exchange, separately from caller cancellation:

```text
caller token cancelled                 → OperationCanceledException
I3HandshakeTimeout expires first       → typed connector timeout
                                         → StageTimedOut / I3Handshake
```

The adapter MUST NOT silently use an unrelated default. It may internally divide connect and Hello work, but the total externally visible budget is the single manager-owned I3 budget. In particular, it passes a remaining bounded connect budget to current `NamedPipeClientSessionOptions.ConnectTimeout` and separately enforces the overall deadline around Hello.

After bootstrap handoff, existing `NamedPipeClientSession.ConnectAsync` remains authoritative for descriptor kind, exact target identity, nonce, protocol negotiation, strict response handling, SID/authentication facts, `ProtocolErrorCode`, and `OperationErrorCode`. Rendezvous does not replace I3. Do not add `session.detach`.

---

## 9. AttachManager lifecycle and operation semantics

### 9.1 State machine

Manager lifecycle per exact ProcessIdentity is:

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

Standalone ProbeAsync does not reserve or transition it. `Orphaned` is target-coordinator/advisory state; the manager maps an unresolved orphan to Settling/CleanupUnknown and reconciles through AttachAsync.

### 9.2 Operation table

| Current state | AttachAsync | ProbeAsync | DetachAsync | Dispose/session behavior |
|---|---|---|---|---|
| Detached | Allocate ID, fresh composition probe; if an authorized orphan condition is observed, reconcile exactly once then fresh probe | Read-only snapshot | Known historical session returns its cached authoritative result; unknown/fabricated session is contract misuse | Manager disposal closes manager only |
| Probing | Fresh ID, `AttachInProgress`, no probe | Allowed, advisory | No session exists | Caller cancellation before strategy entry returns to Detached |
| Bootstrapping | Fresh ID, `AttachInProgress`, no probe | Allowed | No published session | Cancellation prevents publication; tracked settlement |
| Connecting | Fresh ID, `AttachInProgress`, no probe | Allowed | No published session | I3/capability failure or cancellation triggers tracked cleanup |
| Attached | Fresh ID, `AlreadyAttached`, no probe | Allowed | Start/join the one shared stop for the known current-generation session; historical sessions return their own cached result | Dispose local only; may cause client-loss cleanup |
| Detaching | Fresh ID, `AttachInProgress`, no probe | Allowed | Current-generation session joins the same stop task, including after local Dispose if detach was already claimed; historical sessions return their own cached result | Dispose cannot cancel shared stop |
| Settling | Fresh ID, `AttachInProgress`, no probe while owned task runs | Allowed | Current-generation session joins the same stop task; never create second; historical sessions return their own cached result | No new generation until proof/target exit |
| CleanupUnknown | Exactly one reconciliation via valid AttachAsync; no new Host before proof | Advisory | Current-generation session returns cached `CleanupUnknown`; historical sessions return their per-AttachId result; no stop retry | Local disposal cannot clear target gate |
| TargetExited | `TargetExited`, no strategy | `TargetExited` for this known old identity | `TargetExited` for a known, not-preclaim-disposed session | State/terminal metadata inspectable; surfaces closed |

A manager retains the AttachId/session association needed for repeat DetachAsync. A manager lifecycle record of `Detached` is not a contract error when the supplied session is the known historical session for that AttachId; return its current cached authoritative result without touching the current generation. Foreign-manager, unknown, fabricated, or locally disposed-before-detach-claim sessions are programmer/state misuse and throw. For a PID reused by a new process, the new ProcessIdentity is a different map key and an independent target. Old identity never retargets.

### 9.3 Same-manager and different-target concurrency

Same-target calls do not queue. Exact equality is the §4 comparer.

```text
Attached                         → AlreadyAttached
Probing/Bootstrapping/Connecting → AttachInProgress
Detaching/Settling               → AttachInProgress
CleanupUnknown                   → one bounded reconciliation; no Host until closure proof
```

Different exact ProcessIdentity values progress independently. Probe is allowed in every state. Local fail-fast results carry their own fresh AttachId and `AttachStage = LifecycleAdmission`. A foreign manager's fresh probe shows advisory lifecycle state only. When that attempt observes an orphan-reconcilable `Orphaned`, `Settling`, or `CleanupUnknown` generation under §14.2, one authenticated reconciliation is mandatory; the coordinator independently validates authorization and current generation. Otherwise strategy checks the target coordinator before admitting a Host/session and returns its authoritative busy/already-attached result.

### 9.4 Attach publication linearization

No AttachSession exists before I3 authentication and composition validation. Publication is one manager-gated atomic transition:

```text
verify AttachId + exact ProcessIdentity + current operation
verify authenticated client session and Agent SessionId
verify IClrInspectionPort + requested adapter/capabilities
perform final caller-cancellation check
atomically transition Connecting → Attached and publish/store session
```

That transition is the success linearization point. Cancellation observed before it yields OCE, no published session, and tracked cleanup. Cancellation after it cannot undo success; AttachAsync returns success. No execution path both publishes and throws OCE.

Once manager invokes `BeginAttachAsync`, it treats the operation as potentially side-effecting. Caller cancellation then prevents successful publication and transfers control to manager-owned settlement even if the strategy has not yet confirmed its first target-side effect. This conservative boundary removes a race; no strategy task is abandoned.

### 9.5 TargetExited behavior

For an old exact identity whose target is positively known to have exited:

```text
AttachAsync(old identity)       → TargetExited; no strategy
ProbeAsync(old identity)        → TargetExited with prior-incarnation evidence
DetachAsync(old known session)  → authoritative TargetExited, unless the
                                   session was locally disposed before any
                                   detach claim (programmer/state misuse)
old AttachSession.State         → TargetExited
```

If this manager/probe never observed the process and the PID is absent, Probe may return `TargetNotFound`. If the same PID now has another start identity, reject as `TargetIdentityChanged`/`TargetExited` from available evidence; never use the new process for the old request. A new ProcessIdentity with that PID is independently attachable.

---

## 10. Cross-manager serialization and target coordinator

### 10.1 First-entry gate

Before a resident target coordinator owns generation admission, independent managers use a fail-fast cross-process named mutex in the `Global\` namespace. It therefore arbitrates across Windows sessions. `NativeSpy.Attach.Windows` owns it. A1 implements the gate abstraction and Windows adapter; A1 does not perform process entry.

Gate-name input bytes are canonical and identical across managers:

```text
UTF8("NativeSpy.AttachGate.v1")
0x00
ProcessId: signed Int32 little-endian
ProcessStartIdentity: exact UTF-8 bytes, preceded by UInt32 little-endian byte length
```

Name:

```text
Global\NativeSpy.AttachGate.v1.<64 lowercase hexadecimal SHA-256 characters>
```

No culture-sensitive conversion, case folding, Unicode normalization, or raw start identity in the object name. The mutex uses an explicit restrictive DACL allowing only the same-user controller/target identity required by the initial policy; do not use default permissive kernel-object security. Acquisition is immediate and fail-fast, never a queue: failure to acquire yields `AttachInProgress`. The mutex only arbitrates controllers; it does not authenticate the target. Exact ProcessIdentity and target-coordinator checks remain mandatory. The private acquire/release adapter preserves mutex ownership rules across async work; when using a thread-owned mutex, release occurs on its owning thread.

Acquire immediately before first invasive entry. Hold only until coordinator admission/rejection is established or the attempt safely settles, then release. An abandoned mutex is not proof that entry did not happen: revalidate exact ProcessIdentity and query coordinator lifecycle conservatively. Continue only on positive `Detached` evidence; an authorized orphan condition is reconciled once; absent/ambiguous lifecycle evidence fails closed as `CleanupUnknown`, never StartAnyway.

The mutex is transient controller arbitration, not a durable target lifecycle record. C0/C1 own proving that controller loss or mutex abandonment before coordinator admission cannot permit a late duplicate generation; A0 selects no recovery/fence mechanism. Until target state is positively known, the manager fails closed as `CleanupUnknown`.

After coordinator admission the resident target coordinator is the atomic one-active-generation gate. Every later strategy entry checks that state before any new Host work. It rejects a live generation, joins the shared stop, or permits bounded authorized orphan recovery; it never creates a second Host while closure is uncertain.

### 10.2 Resident coordinator

A small process-global NativeSpy coordinator may remain resident across generations. Exact anchoring is B0/C0; an accidental per-AssemblyLoadContext coordinator is insufficient.

Permitted state only:

```text
bootstrap version
one-active-generation gate and current lifecycle state
current AttachId + exact ProcessIdentity while active/settling
current AgentHost reference while active/settling
one shared start task and one shared stop task per generation
composition/readiness status
private LifecycleOwnerProof while not terminal
bounded terminal metadata
```

Forbidden: object/member/type registries, shared handle namespace, reconnect state, multi-client Host, ObjectSpy/FlaUI state, leases, persistent object registry, or reuse of old session material. After positive Host closure, clear coordinator roots to old AgentHost, ClrAgentSession, composition, registries, adapters, and secrets. Stack-local references from already-running callbacks may remain naturally until those callbacks finish.

The AttachManager owns public lifecycle policy/results. The resident coordinator is the target-side atomic generation gate and sole executor of its one shared Host stop task. It is not a second public manager or a broker.

---

## 11. Cancellation and timeout ownership

### 11.1 Separate budgets

`AttachTimeouts` is manager configuration, not duplicated on AttachRequest. It supplies distinct finite positive budgets for:

```text
Probe
NativeProcessEntry
BootstrapStartup
Rendezvous
AgentHostReadiness
I3Handshake
DetachOperation
DetachCallerWait
CleanupSettlement
Reconciliation
```

Each has one semantic owner. No A0 production duration is prescribed. A1 tests inject deterministic small budgets. `Timeout.InfiniteTimeSpan`, zero, and negative values are invalid configuration. A stage cannot silently borrow another stage's budget.

The I3 connector request explicitly carries `I3HandshakeTimeout` (§8.3). Stage expiration and caller cancellation are distinct.

### 11.2 Cancellation

Caller cancellation for ProbeAsync, AttachAsync, or DetachAsync is normal `OperationCanceledException` behavior.

Before manager enters `BeginAttachAsync`, cancellation ends local work with no target-entry operation. From strategy invocation onward, the attempt is potentially side-effecting: caller cancellation sets an irreversible abandoned flag, cancels the active advance-stage token, prevents all later attach stages and publication, and returns OCE to that caller. The manager retains the attempt in its visible per-target record while it performs bounded cleanup on an independent manager-owned lifetime. Same-target attach stays blocked until closure is established or state becomes CleanupUnknown. No forced remote-thread abortion and no untracked background attach.

If a stage deadline expires after side effects may have begun, cancel that stage through manager-owned operation control and begin cleanup. Strategy/connector work must observe its finite deadline and return a typed state; if native target work cannot be stopped, its uncertainty is represented by CleanupUnknown and a bounded later reconciliation, not an indefinitely waiting manager task. Late results are consumed only under the old operation identity and cannot publish. Stage timeout is typed, not caller OCE.

### 11.3 Detach timeout

- `DetachCallerWait` is only one caller's wait bound. Its expiry returns a typed non-authoritative Settling observation; it is not cached as target truth.
- Caller cancellation ends only that caller's wait by OCE.
- `DetachOperation` bounds the shared detach request/stop operation. Expiry does not return state to Attached; session becomes terminal, state remains Settling while shared cleanup is known to continue.
- `CleanupSettlement` then bounds the manager-owned wait for positive closure. On expiry without proof, the manager stops waiting, records visible `CleanupUnknown`, and new attach remains blocked. The target coordinator's already-started shared stop may finish naturally, but no controller polling/task continues beyond the bounded settlement; a later valid AttachAsync performs one bounded reconciliation.
- Caller-wait expiry never cancels shared cleanup. Positive closure observed while the bounded shared task is active updates the authoritative cache; after the manager has entered CleanupUnknown, a later bounded reconciliation can update it. No endless polling loop exists.

---

## 12. Bootstrap, rendezvous, and secret custody

### 12.1 Initial bootstrap flow

Target-side flow is:

```text
validate exact target incarnation
acquire resident coordinator lifecycle gate
create AgentHostOptions on target side
    (current CreateDefault factory creates pipe name + bootstrap nonce)
AgentHost starts and owns its existing descriptor/I3 behavior
return internal descriptor + AttachId correlation over rendezvous
close one-use rendezvous
```

The controller does not choose Agent pipe name, bootstrap nonce, or target AllowedUserSid. This corrects current ownership without pretending the baseline already does it. The rendezvous is short-lived lifecycle/bootstrap delivery, not a permanent RPC system. AgentHost continues to own the I3 pipe endpoint and nonce lifecycle after target-side creation of its options.

### 12.2 Rendezvous rules

Every bootstrap, explicit detach, or reconciliation uses a fresh one-use attach-layer named-pipe rendezvous. Direction is fixed:

```text
controller / AttachManager creates the Named Pipe server endpoint
resident target bootstrap/coordinator connects as the Named Pipe client
GetNamedPipeClientProcessId on the controller-side server observes the target PID
```

This is separate from the later AgentHost/I3 pipe, where AgentHost is the server and the controller is the client. Each rendezvous has:

```text
one use
fresh cryptographic token
fresh AttachId correlation (attempt/generation fields distinct)
exact expected ProcessIdentity
actual-controller-identity ACL
same-user + equal-integrity initial policy
finite Rendezvous timeout
replay rejection
bounded message schema
```

The endpoint allows at most four malformed/wrong-secret unauthenticated attempts before retirement, and at most one accepted authenticated operation. Wrong secrets consume only the bounded attempt budget. A correct secret with wrong operation, generation, or identity is security-significant: fail closed and retire immediately.

Authenticated messages carry/echo bounded operation kind, `AttemptAttachId` (the current explicit AttachAsync attempt when one exists), `TargetGenerationAttachId` (the generation acted upon; equal to AttemptAttachId on successful start and the old generation on detach/reconcile), exact ProcessIdentity, requested composition where applicable, and a one-operation request correlation ID. A current-orphan reconcile with no known target-generation ID binds it atomically at coordinator acceptance and echoes the actual generation before completion. Self-reported PID/identity is consistency evidence only. AttachIds are never authentication.

### 12.3 Peer identity and ownership

`NativeSpy.Transport.NamedPipes` owns only the low-level connected peer PID observation using `GetNamedPipeClientProcessId`. It knows no AttachId, expected target, or authorization policy. On the controller-created server endpoint, the connected target is the Named Pipe client. `NativeSpy.Attach.Windows` owns:

```text
OS-observed client PID
→ re-read exact ProcessIdentity for that PID
→ compare with expected target ProcessIdentity
→ continue only on exact match
```

Failure to obtain PID, read identity, or compare exact identity is fail-closed. Bootstrap-claimed PID/identity alone never authenticates the peer. The low-level primitive and production integration are B1, not A1.

The controller's rendezvous ACL is derived from its own actual primary SID and grants only the required controller/user, creator, and system principals; it does not inherit a broad Everyone/Anonymous default. The target derives AgentHost `AllowedUserSid` from its actual target primary token. Initial policy is equal SID and equal integrity; a same-user mismatch of integrity is `KnownUnsupported`. DACL/MIC/process access checks can still fail, and there is no automatic elevation.

### Security threat outcomes

| Threat | Required outcome |
|---|---|
| Guessed endpoint | Random one-use endpoint, restrictive actual-controller ACL, fresh token; no auth means no accepted operation |
| Stolen/replayed rendezvous message/token | OS-observed peer PID→exact ProcessIdentity, fresh single-use token, operation/generation match, atomic consumption, endpoint retirement; replay rejected |
| AttachId guessed or disclosed | No authority; separate rendezvous token and/or owner proof required |
| Stale lifecycle response | Exact process, attempt, target-generation AttachId, request ID, and current manager operation must match |
| Same-user competing manager | Fail-fast first-entry gate; resident coordinator rejects live generation; no second Host |
| PID reused / target restarted | Exact process-start identity mismatch; retained validated process object prevents retarget |
| Descriptor from wrong process | Rendezvous peer check and existing I3 identity/nonce validation fail closed |
| Wrong-user peer | DACL/SID policy rejects; equal primary SID is required |
| Live-session takeover | Foreign request rejected while original authenticated I3 client is live; owner proof is never inferred from AttachId |
| Old A reconciliation arriving after B | A-tagged generation fails current-generation comparison; no stop/reset/publication into B |
| Caller-supplied target SID | Ignored/rejected as authority; target derives AllowedUserSid from its own primary token |

A compromised same-user process may cause denial of service by contending for a gate or exhausting bounded invalid attempts; it cannot gain a successful NativeSpy session or authorize a different generation by these mechanisms.

### 12.4 Secrets and non-secrets

| Material | Creator / scope | Transfer and validator | Lifetime/destruction | Public/reusable? |
|---|---|---|---|---|
| AttachId | AttachManager; one explicit attempt/generation | Correlation only; exact generation checks | Bounded terminal metadata | Public correlation; not secret, not authorization |
| Rendezvous token | Controller manager; one rendezvous | Private bootstrap/lifecycle message; target compares in constant time after ACL/peer checks | Zero/retire on terminal rendezvous; never reused | Never public; one use |
| AgentHost bootstrap nonce | Target-side Host options factory; one Host/I3 bootstrap | Internal descriptor; existing AgentHost validates in I3 Hello | Clear under Host/session terminal cleanup | Never public; one handshake/session |
| LifecycleOwnerProof | Resident coordinator; one admitted generation | Private bootstrap handoff to original manager; coordinator validates for owner-authorized detach/reconcile | Retain while generation/owner operation unresolved; clear at positive terminal closure/target exit | Never public; not AttachId; not reused |
| Controller/target SID and integrity | OS primary tokens | Evidence/policy only | Not secret attach credentials | Never caller-selected |

AttachId guessing grants nothing. LifecycleOwnerProof is separate secret material. A foreign orphan-recovery request does not use AttachId as authorization; it requires fresh authenticated rendezvous and coordinator state proving the original authenticated client is absent (§15).

---

## 13. Detach, Dispose, client loss, and shared stop

### 13.1 Authoritative detach

Successful `DetachAsync(session)` means at minimum:

```text
AgentHost close positively verified
ClrAgentSession closed
framework composition disposed
Agent session/pipe endpoint closed
old coordinator generation roots cleared
old local operational surfaces terminal
target application still running
```

Complete unloading of all NativeSpy modules is not required; resident bootstrap may remain. `State == Closed` alone is insufficient if disposal failures were swallowed.

Authoritative target lifecycle detach is delivered through a fresh one-use authenticated attach-layer lifecycle rendezvous naming exact AttachId and ProcessIdentity. Local client-pipe closure is not detach confirmation. Do not add `session.detach` to I3.

### 13.2 Shared stop and detach/client-loss linearization

There is one shared target stop/settlement operation per generation. The coordinator atomically transitions:

```text
Live(A) → Stopping(A, cause)
```

The first accepted trigger sets `cause = ExplicitDetach` or `ClientLoss`. Later triggers join the same task and never start a second stop.

- If explicit detach wins, later client loss joins it. Positive Host closure allows DetachAsync to return authoritative Detached.
- If client loss wins, the coordinator independently knows the authenticated client is gone and starts the shared cleanup. Later owner DetachAsync may join/observe through the authorized lifecycle path. It does not start another stop. Pipe loss alone is not a successful DetachAsync result.
- Neither path returns the generation to ordinary Attached.

For a known manager-owned AttachId/session, first DetachAsync starts/joins the single shared operation; later calls join it or return the cached operation/current authoritative result. Cache `Detached`, `TargetExited`, and current `CleanupUnknown`. A shared `DetachFailed` observation with `Settling` is also returned on repeats without issuing another stop. When authorized reconciliation later proves `CleanupUnknown → Detached` for that same AttachId, update its cached result; subsequent DetachAsync returns the updated result and performs no stop work. Caller timeout/cancellation is not cached as target result.

### 13.3 Dispose and local linearization

`AttachSession.DisposeAsync()` is idempotent local terminal cleanup only. It may close local client resources/transport and thereby cause target-side client-loss cleanup. It MUST NOT claim successful detach or release the target gate by assumption.

The manager/session gate has exactly two competing transitions from Active:

```text
Detach claims first: Active → Detaching
    Dispose closes local resources but cannot replace/cancel the shared detach.
    Repeated DetachAsync for this already-claimed AttachId still joins/observes
    that operation or its cached authoritative result, even after local Dispose.

Dispose claims first: Active → LocallyDisposed
    close local client resources; DetachAsync(session) is programmer/state
    misuse and throws unless detach had already been claimed for this AttachId.
    Manager/coordinator still owns target cleanup/reconciliation.
```

Use the state precedence in §7.3: positive `TargetExited` or `Detached` target facts supersede local-only disposal; absent either fact, Dispose projects `LocallyDisposed` (including while target closure is `CleanupUnknown`). Local disposal never clears the manager's target lifecycle record. A disposed session never revives. Normal flow is `manager.DetachAsync(session)` then `session.DisposeAsync()`.

`AttachManager.DisposeAsync` closes the manager to new calls (`ObjectDisposedException`). It cancels pre-side-effect local work, tracks cleanup for side-effecting unpublished work, locally closes an active client without claiming target detach, and does not cancel a shared detach already claimed. It is not a DetachResult and never assumes gate release.

### 13.4 Client disappearance

Unexpected I3 client/pipe loss is not successful DetachAsync. The resident coordinator transitions the generation to orphaned/settling and starts/joins its one shared stop/reconcile operation. Once Host closure is positively confirmed, lifecycle becomes Detached and explicit fresh attach is allowed. If closure is not established, it remains Settling then CleanupUnknown.

Pre-I3 controller disappearance after generation admission is an unpublished generation needing cleanup. AgentHost pre-handshake retry/listening behavior does not permit a second generation. Target-side bounded expiry/stop and manager-owned cleanup must reach verified closure or CleanupUnknown.

---

## 14. CleanupUnknown and cross-controller orphan recovery

### 14.1 CleanupUnknown

`CleanupUnknown` visibly blocks admission/start of another AgentHost/session. It is not a force-reset permission or hidden permanent background poll.

A later valid AttachAsync is the sole public trigger for one bounded reconciliation:

```text
validate request; allocate fresh AttachId
perform exactly one ReconcileAsync
    closure positively proven → lifecycle Detached; fresh composition probe;
                                continue same attempt only if Eligible
    target positively exited → TargetExited
    unresolved/timeout        → CleanupUnknown; no BeginAttachAsync/new Host
```

No public `ReconcileAsync`, `ForceReset`, `ForgetHost`, `StartAnyway`, target kill, second Host, or endless polling. An already-running Settling operation causes same-manager fail-fast; no redundant reconciliation until it becomes CleanupUnknown.

A reconciliation response is tagged with the current Attach attempt ID, exact ProcessIdentity, and target generation observed/acted on. A late result for A cannot mutate B. The coordinator checks and transitions the generation atomically; stale A operations are rejected if a newer generation is current. No response is accepted merely because it knows an AttachId.

### 14.2 Foreign manager rule

A foreign/same-user manager may not stop or reconcile a live authenticated session. Foreign reconciliation is permitted only when the target coordinator has independently established that the original authenticated client is absent and the current generation is already `Orphaned`, `Settling`, or `CleanupUnknown` arising from that absent-owner generation. A CleanupUnknown generation whose owner is still live may be reconciled only by the owner-authorized operation carrying `LifecycleOwnerProof`:

```text
original authenticated owner session still live
    → foreign reconciliation rejected; owner proof required

coordinator independently established original client absence and the
current generation is Orphaned / eligible Settling / eligible CleanupUnknown
    → fresh same-user authenticated one-use rendezvous may request exactly
      one bounded current-orphan reconciliation
```

“Owner live/absent” is the target coordinator's authenticated I3 owner-session state, not a PID claim in an ordinary probe. The controller process can remain alive after its client session is gone; the coordinator's independent loss observation, not process liveness assertion by the requester, is the prerequisite. Same-user policy, OS-observed peer PID→exact ProcessIdentity, fresh rendezvous token, exact target identity, and current-generation validation are all required. Knowing AttachId alone never authorizes recovery. The manager MUST perform the one reconciliation when its admitted attach observes an eligible orphan state; it is not optional.

A current-orphan operation is atomically bound to the orphan generation at coordinator acceptance. It cannot stop a live B. If an A stop/reconcile already began, its task/result remains A-tagged and cannot publish state into B. A subsequent Attach attempt that observes an eligible orphan MUST separately reconcile the orphan current at its own atomic acceptance point; that is a new operation, not reuse of A's result.

---

## 15. Fresh reattach and late callbacks

Each successful explicit reattach creates fresh:

```text
AttachSession
AttachId
AgentHost
Agent SessionId
ClrAgentSession
registries and handles
framework composition
adapter instances
```

Old handles, members, types, continuation tokens, framework evidence, adapters, and callback state never revive. No reconnect restores an old logical session.

Releasing A's one-active-session gate does not wait indefinitely for arbitrary already-started target callbacks. Such callbacks are not forcibly aborted and may naturally mutate the target application's own state. They MUST NOT:

```text
respond into B
create handles in B
mutate B registries
use/adopt B transport, session, composition, or adapters
change B lifecycle/gate
be interpreted as B results
```

Generation-tagged publication and session-bound objects enforce this. Once Host closure is established, coordinator roots to A are cleared; old stack-local references may finish naturally but cannot be adopted by B.

---

## 16. Identity and stale-work guards

No single generation substitutes for another:

| Identity | Names | Required guards |
|---|---|---|
| ProcessIdentity | Exact OS process incarnation | Before/after probe, pre-entry same-process-object validation, rendezvous peer PID reread, detach/reconcile target |
| AttachId | One explicit attach attempt/generation | Manager results, strategy requests/results, publication, detach, coordinator generation checks |
| Private manager operation identity | Current in-process operation instance | Reject stale task completion after cancellation/disposal; not public/authentication |
| Agent SessionId | One authenticated Agent session | Connector completion and session-bound CLR calls |
| HandleId/MemberId/TypeId and generations | Session-local CLR identities | Existing I3/Agent validation; never cross session |
| ObjectSpy external-selection generation | UIA selection/finder/preview work | Reject stale external results |
| ObjectSpy CLR navigation epoch | CLR browser/navigation work | Reject stale CLR results; also require current AttachId when continuity matters |

Required commit guards:

```text
probe evidence commit                  → exact identity before + after evidence
pre-entry                              → exact identity from retained process object
rendezvous peer                        → OS peer PID → exact identity compare
strategy/connector result              → AttachId + ProcessIdentity + current op
session publication                    → above + Agent SessionId + final cancellation check
session terminal callback              → AttachId + ProcessIdentity + Agent SessionId
adapter callback                       → AttachId + original authenticated-session identity
CLR result                             → existing Agent SessionId + CLR IDs/generations
ObjectSpy UIA result                   → external-selection generation
ObjectSpy CLR navigation result        → CLR navigation epoch + current AttachId
```

FlaUI does not learn AttachId; ObjectSpy owns pairing and stale-work rejection.

---

## 17. Failure taxonomy and safe diagnostics

### 17.1 Support versus operational failure

Support disposition is not an operational error. It is one of `KnownSupported`, `KnownUnsupported`, `Unknown` (plus composition `NotRequested`). Unknown differs from Unsupported and rejects initial attach without invoking `BeginAttachAsync`.

The attach-domain failure codes are:

```text
TargetNotFound
TargetExited
TargetIdentityChanged
AccessDenied
UnsupportedArchitecture
UnsupportedRuntime
UnsupportedComposition
UnsupportedSecurityContext
ProbeInconclusive
AlreadyAttached
AttachInProgress
StageTimedOut
BootstrapFailed
RendezvousFailed
RendezvousPeerVerificationFailed
ClientSessionEstablishmentFailed
CapabilityMismatch
CleanupFailed
CleanupUnknown
InternalFailure
```

These are typed expected outcomes. No ordinary exception for expected target exit, access denial, unsupported target/security context, same-target conflict, bootstrap timeout, or detach failure.

After I3 begins, exact `ProtocolErrorCode` and `OperationErrorCode` remain lower-layer facts. Attach wraps them; it does not invent duplicate protocol/authentication meanings.

### 17.2 Stage ownership and canonical mappings

Stages are:

```text
LifecycleAdmission
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

| Condition | Public result | Post-failure lifecycle |
|---|---|---|
| known Attached | `AlreadyAttached / LifecycleAdmission` | Omitted; existing Attached unchanged |
| known Probing/Bootstrapping/Connecting/Detaching/Settling | `AttachInProgress / LifecycleAdmission` | Omitted; existing operation unchanged |
| pre-Host BeginAttach coordinator finds current live Attached | `AlreadyAttached / LifecycleAdmission` | Omitted; existing generation unchanged; no Host |
| pre-Host BeginAttach coordinator finds current non-reconcilable transition | `AttachInProgress / LifecycleAdmission` | Omitted; existing generation unchanged; no Host |
| known exited incarnation | `TargetExited` at current stage | TargetExited |
| never-observed PID absent | `TargetNotFound / Probe` | Detached |
| exact identity mismatch | `TargetIdentityChanged` | Detached or TargetExited only if proven |
| required access denied | `AccessDenied` at evidence stage | Detached before effects; otherwise tracked settlement |
| positively known architecture unsupported | `UnsupportedArchitecture / Probe` | Detached; no strategy |
| positively known runtime unsupported | `UnsupportedRuntime / Probe` | Detached; no strategy |
| positively known composition unsupported | `UnsupportedComposition / Probe` | Detached; no strategy |
| positively known security context outside policy | `UnsupportedSecurityContext / Probe` | Detached; no strategy |
| required security/token evidence denied by Windows | `AccessDenied / Probe` | Detached; no strategy |
| security evidence inconclusive without access denial | `ProbeInconclusive / Probe` | Detached; no strategy |
| any other required support dimension unknown | `ProbeInconclusive / Probe` | Detached; no strategy |
| probe stage deadline | `StageTimedOut / Probe` | Detached; no BeginAttach |
| pre-entry identity cannot be trusted | `TargetIdentityChanged` or `AccessDenied / PreEntryValidation` as evidenced | Detached; no entry |
| process entry/bootstrap/readiness failure | typed stage code | Detached only if no effects or closure proven; else Settling/CleanupUnknown |
| rendezvous peer PID/identity verification failure | `RendezvousPeerVerificationFailed / Rendezvous` | Preserve actual post-failure lifecycle; never claim Detached without proof |
| other rendezvous failure | `RendezvousFailed / Rendezvous` | Detached only if no effects or closure proven; else Settling/CleanupUnknown |
| I3 connection/handshake failure | `ClientSessionEstablishmentFailed / I3Handshake`; preserve lower error | Tracked cleanup |
| I3 handshake deadline | `StageTimedOut / I3Handshake` | Tracked cleanup |
| required WinForms/CLR capability missing | `CapabilityMismatch / CapabilityValidation` | Tracked cleanup; no session published |
| locally known CleanupUnknown reconciliation is unresolved or times out | `CleanupUnknown / Reconciliation` | CleanupUnknown; no BeginAttach |
| mandatory advisory-orphan reconciliation proves Detached | No failure; perform a new fresh probe in the same attempt before any BeginAttach | Lifecycle becomes Detached |
| mandatory advisory-orphan reconciliation proves target exit | `TargetExited / Reconciliation` | TargetExited; no BeginAttach |
| mandatory advisory-orphan reconciliation finds current live Attached generation | `AlreadyAttached / Reconciliation` | Omitted; existing generation unchanged; no BeginAttach |
| mandatory advisory-orphan reconciliation finds current non-reconcilable transition | `AttachInProgress / Reconciliation` | Omitted; existing generation unchanged; no BeginAttach |
| mandatory advisory-orphan reconciliation completes without closure/exit proof | `CleanupUnknown / Reconciliation` | CleanupUnknown; no BeginAttach |
| mandatory advisory-orphan reconciliation budget expires | `StageTimedOut / Reconciliation` | CleanupUnknown; no BeginAttach or background polling |
| cleanup fails with no earlier causal failure | `CleanupFailed / Cleanup` | Settling or CleanupUnknown |
| original attach failure plus cleanup failure | retain original failure + bounded cleanup code | Settling/CleanupUnknown |
| caught operational infrastructure failure | `InternalFailure` at owning stage | according to effects; never claim Detached without proof |
| programmer contract/internal invariant violation | throw/fault after required safe settlement | never disguise as target-domain result |
| caller token cancellation | `OperationCanceledException` | cleanup rules in §11 |

Expected platform/transport failures are translated at their owning adapter. Rendezvous transport, authentication, and peer-verification failures during reconciliation retain the existing `RendezvousFailed / Rendezvous` or `RendezvousPeerVerificationFailed / Rendezvous` mapping from the rendezvous-owning stage; they are not lifecycle conflicts. The manager-owned reconciliation budget expiry is `StageTimedOut / Reconciliation`, distinct from an inner rendezvous-stage failure. `InternalFailure` is reserved for an unexpected caught operational/infrastructure failure requiring a safe public typed result. It is not a blanket `catch (Exception)` for argument errors, programmer bugs, or violated manager invariants; those fault/throw after tracked settlement.

### 17.3 Diagnostics

Diagnostics are bounded allowlisted values only:

```text
AttachStage
DiagnosticId: at most 64 ASCII chars [A-Za-z0-9._-]
Win32 error / HRESULT
architecture and structured runtime evidence
support disposition
ProtocolErrorCode / OperationErrorCode when authoritative
```

Probe limitations are at most 16 fixed enum values. No `Exception.ToString()`, stack trace, arbitrary exception message, target command line/environment, raw memory/address, target-provided text, or unbounded file path is public.

---

## 18. Partial-attach cleanup matrix

“Lifecycle owner” is the sole decision owner for that row; resource executors do not gain public lifecycle authority. Before target coordinator admission, AttachManager owns attempt state and strategy executes local resources. After admission, AttachManager still owns public result/state while the target coordinator serializes and executes its one shared Host stop task. Client-loss autonomic stop is initiated by that coordinator and reported to the manager.

| Stage | Resources that may exist | Lifecycle owner / cleanup | Result and possible residue | Reattach |
|---|---|---|---|---|
| Probe complete | Immutable evidence; local query handles | AttachManager; close observations | Unsupported/Unknown/timeout; no target residue | Yes after operation release |
| Validated target binding | Strategy-private process object | AttachManager directs strategy to close it | Access/identity failure; no public handle | Yes after Detached |
| Entry gate acquired | Same-user `Global\` named mutex | AttachManager operation; strategy releases only after coordinator admission/rejection or safe settlement | Contention `AttachInProgress`; abandoned ambiguity `CleanupUnknown` | Only after gate/lifecycle evidence |
| Native/process entry begun | Native-entry resources, possible executing target entry | AttachManager tracks; strategy cleans its own resources; coordinator owns any admitted generation | Failure/timeout; no force-abort; uncertain entry blocks | Only after positive closure or target exit |
| Rendezvous created | Controller endpoint + fresh token | AttachManager/strategy for this attempt; close endpoint and zero token | `RendezvousFailed`/timeout; no persistent channel | Yes only when no target effect or closure proven |
| Target bootstrap reached, not admitted | Bootstrap attempt and endpoint | AttachManager; target startup expiry/one-shot failure cleanup | Typed bootstrap failure; bounded bootstrap residue only | After Detached proof; else blocked |
| Coordinator created/admission pending | Coordinator gate and generation metadata | Target coordinator serializes; manager tracks result | Settling/Unknown if admission/closure uncertain | No second generation until proof |
| AgentHost constructed | Options, nonce, composition, Host object | Coordinator shared stop; AgentHost executes disposal | Bootstrap failure; nonce cleared at terminal; no session published | Only after Host close proof |
| AgentHost started/readiness pending | Pipe listener, composition, CLR session | Coordinator's one shared stop task | Settling then CleanupUnknown if closure unproven | Blocked |
| Descriptor returned | Internal descriptor + nonce handoff | Coordinator owns generation; manager owns connect/cleanup decision | Descriptor invalidated on failed handoff; never public | Blocked until close proof |
| I3 handshake started | Local pipe/session attempt + target Host | Connector closes local resources; manager requests coordinator stop | `ClientSessionEstablishmentFailed`/timeout; Host may remain only while tracked | Blocked until close proof |
| I3 authenticated | Agent SessionId, CLR registries, composition | Manager withholds publication until validation; coordinator executes stop on failure | No partial AttachSession; fresh cleanup required | Blocked until close proof |
| Composition validation failed | Authenticated session + missing requested adapter | Manager returns `CapabilityMismatch`; coordinator stops generation | No generic downgrade; no published session | Blocked until close proof |
| AttachSession published | Active session and adapters | Manager owns public lifecycle; coordinator owns target gate/stop task | Attached generation only | After authoritative detach/target exit |
| Explicit detach/client loss | One shared per-generation stop task | Coordinator atomically chooses first cause; manager callers join | Detached only on positive closure; otherwise Settling/Unknown | Only after positive closure |
| Host closure verified | No active Host/session/composition roots | Coordinator clears generation roots; manager commits Detached | Resident bootstrap may remain | Yes, fresh identities |
| Target exits | Process and generation gone | Manager/coordinator terminalize exact identity | `TargetExited`; never successful app-preserving detach | New ProcessIdentity only |

A timeout or caller cancellation never frees the gate by assumption. A controller crash with an abandoned gate is handled under §10.1. No arbitrary callback is forcibly aborted.

---

## 19. Race-condition outcomes

| Race | Deterministic outcome |
|---|---|
| Two same-manager attaches to same target | First claims operation; second gets fresh AttachId + `AttachInProgress`, no probe |
| Different targets attach concurrently | Independent state/gates |
| Two managers race first entry from any Windows sessions | Canonical `Global\` mutex admits one; loser fails fast; winner checks coordinator before Host start |
| Attach A cancelled while bootstrap later completes | A cannot publish; manager tracks late result and requests shared cleanup; A remains blocking until settlement; A result cannot commit to B |
| Detach A races client pipe loss | Coordinator CAS chooses one initial cause and one shared stop task; later trigger joins |
| Detach caller times out, Host closes one millisecond later | Caller gets non-authoritative Settling timeout; shared result becomes/caches Detached; next call observes Detached |
| Reconciliation A races new attach B | Coordinator compares current generation atomically; stale A cannot stop/change B; results carry A and manager rejects stale operation |
| Target exits during bootstrap | Exact process-object/identity checks fail; TargetExited; no retarget |
| Target exits during detach | TargetExited, not successful detach |
| Probe races restart/PID reuse | Before/after identity mismatch discards evidence; no entry authorization |
| Client crashes after Host starts before I3 | Unpublished generation enters tracked settling; coordinator/expiry stops Host; no second generation before closure |
| Composition validation fails after I3 | No AttachSession publication; request shared cleanup; no generic downgrade |
| Dispose races Detach | Gate linearization in §13.3 selects one winner; local Dispose never claims target detach |
| Late strategy result A arrives after B starts | AttachId + ProcessIdentity + manager-operation check rejects it; target coordinator also rejects stale generation |
| Neutral or Unknown probe precedes attach | Neutral is non-eligible; Unknown blocks BeginAttach |

---

## 20. ObjectSpy, WinForms, compatibility, and platform facts

### 20.1 ObjectSpy/FlaUI pairing

AttachManager is unaware of FlaUI. ObjectSpy owns pairing an external UIA session with CLR AttachSession, and requires the same exact ProcessIdentity, not PID equality. ObjectSpy may use AttachId to reject async work crossing detach/reattach; FlaUI itself need not know AttachId.

The repository's PID-only FlaUI attach must be corrected in E0/E1 with an exact-process-incarnation validation seam. The UIA identity stays in FlaUI; CLR identity stays in Agent. Existing external-selection generation and CLR-navigation epoch remain distinct from AttachId.

### 20.2 WinForms boundary

`NativeSpy.Attach` contracts remain framework-neutral. `NativeSpy.Attach.Windows` owns OS probing, rendezvous, and native entry, not WinForms semantics. Client-side detached WinForms correlation remains in `NativeSpy.Client`; target-side evidence remains `NativeSpy.Agent.WinForms`.

Current real-target problems remain explicit I5 requirements, not A0/A1 solutions:

```text
execution-context discovery
correct UI thread
multiple WinForms UI threads
anchor destruction/recreation
HWND-to-correct-context routing
```

E0 defines architecture; E1 implements an arbitrary supported WinForms target without requiring a supplied `Form` or `Control`. WPF remains I6.

### 20.3 Windows documented facts versus inference

- `GetNamedPipeClientProcessId` returns the connected client PID, not authenticated application identity or process incarnation. Attach must reread exact ProcessIdentity. [Microsoft API](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid)
- Named-pipe security descriptors control DACL access; default ACLs are not the required same-user policy. MIC is an additional access check. [Named pipe security](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights), [MIC](https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control)
- `IsWow64Process2` reports process/native machine values and documents required query rights. `OpenProcess`/token APIs can fail due process security/access checks; no universal access guarantee exists. [Process access](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights)
- PSAPI module enumeration/path APIs report module observations and have documented failure/race limitations; they do not establish application TFM, deployment model, or by themselves prove runtime identity/version. Any support threshold is NativeSpy policy; A1 returns `Unknown` when observed evidence cannot reliably bind runtime identity/version. [EnumProcessModulesEx](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-enumprocessmodulesex), [GetModuleFileNameEx](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getmodulefilenameexw)
- MIC can block lower-integrity writes despite a DACL. The equal-integrity rule is our conservative scope policy, not a general platform necessity.

### 20.4 .NET hosting and TFM facts

The current official native-hosting tutorial states that `nethost`/`hostfxr` APIs support framework-dependent deployment; self-contained deployments are treated as stand-alone executables. It also states the hosting constraint for `hostfxr_initialize_for_runtime_config`: only one runtime can be loaded in a process; another initialization reuses it if compatible or fails if incompatible. This constraint is scoped to the documented native-hosting API behavior; do not generalize it to unrelated hosting designs. [Microsoft: Write a custom .NET runtime host](https://learn.microsoft.com/en-us/dotnet/core/tutorials/netcore-hosting)

A TFM describes build-time API targeting; runtime selection/roll-forward and self-contained deployment are separate. Therefore observed CoreCLR version does not prove TFM, deployment layout, or payload compatibility. [Target frameworks](https://learn.microsoft.com/en-us/dotnet/standard/frameworks), [.NET runtime selection](https://learn.microsoft.com/en-us/dotnet/core/versions/selection)

No A0 decision selects multi-targeting, runtime-specific payload sets, hostfxr strategy, or self-contained support. D0 owns broader payload architecture.

---

## 21. Iteration ownership

```text
I5-A0  this architecture freeze
I5-A1  contracts, manager/fakes, read-only Windows probe, deterministic tests
I5-B0  cooperative bootstrap/rendezvous/lifecycle architecture
I5-B1  real cooperative bootstrap, one-use rendezvous, I3 connect,
       target-derived AllowedUserSid, peer-PID verification,
       LifecycleOwnerProof, AgentHost shared StopAsync fix before
       detach/reattach acceptance, authoritative detach/reattach
I5-C0  NativeSpy-owned x64/.NET 10 process-entry architecture, including the
       exact runtime evidence required to authorize the first real entry and
       crash/abandonment behavior before coordinator admission
I5-C1  x64/.NET 10 native process-entry implementation and proof of same-object
       process binding, C0 runtime evidence, and no late duplicate entry
I5-D0  broader runtime/payload compatibility architecture
I5-D1+ supported modern-.NET compatibility implementation
I5-E0  arbitrary WinForms execution-context architecture
I5-E1  real arbitrary WinForms ObjectSpy path + exact FlaUI identity integration
I5-F   lifecycle/security/access/partial-cleanup/race/stress/product hardening
I5-G   optional x86, .NET Framework, ARM64, launch-time strategies
I4b    Object Browser foundation
I6     WPF target support
```

Do not collapse milestones. C0/C1 process entry is separate from D runtime compatibility and E WinForms execution-context discovery. I5 is not complete until arbitrary supported WinForms applications work without supplying NativeSpy a Form or Control.

Known debt placement:

| Debt | Owner |
|---|---|
| AgentHost StopAsync task-idempotence and truthful closure | Required B1 prerequisite |
| Target-derived AllowedUserSid/bootstrap API | B0 design, B1 implementation |
| Real peer-PID rendezvous | B0 design, B1 implementation |
| LifecycleOwnerProof creation, transport, and validation | B0 design, B1 implementation |
| Native process-handle rights/entry and first-entry runtime evidence threshold | C0/C1 |
| FlaUiFrozenSelection deterministic release | Revisit in E1 when longer-lived integrated sessions require it |
| ObjectSpy concrete FlaUI coupling | Reconsider at I4b/I5 integration; exact identity seam by E1 |
| Stateless member-page reconstruction cost | Defer until profiling |
| Mixed-DPI validation | Validation backlog, not attach architecture |

---

## 22. Exact I5-A1 implementation contract

### 22.1 A1 allowlist

A1 MAY modify/add only what is necessary within:

```text
src/NativeSpy.Attach/
src/NativeSpy.Attach.Windows/
tests/NativeSpy.Attach.Tests/
tests/NativeSpy.Attach.Windows.Tests/

src/NativeSpy.Client/
    minimal IAttachAdapter and authenticated connector/session contracts,
    semantic DTOs owned there, and required friend-assembly declarations

src/NativeSpy.Client.NamedPipes/
    minimal adapter implementation over existing NamedPipeClientSession

solution/project references necessary to add the above projects
```

Within that allowlist, A1 may implement:

```text
strict request/result DTOs and result unions
ProcessIdentity comparer and reader-compatible validation
AttachId allocation/result correlation
composition-ID and adapter-ID validation
TargetRuntimeIdentity and structured numeric version
architecture/runtime/composition/security dispositions
ProbeOutcome and overall eligibility
bounded diagnostics and bounded capabilities
AttachTimeouts finite validation
IAttachManager and exact public lifecycle semantics
IAttachStrategy exact semantic operations/results
internal authenticated connector/session seam and fake connector/session
AttachSession states, typed adapter lookup, ownership/disposal
AttachManager state, serialization, publication and stale-result guards
CleanupUnknown bounded reconciliation semantics with fakes
task-idempotent fake detach and per-AttachId shared result semantics
client-terminal callback generation guard
cross-manager Global first-entry mutex and canonical-name tests
private validated-target seam and fake binding
read-only Windows architecture/runtime/token/security probe
fakes and deterministic tests in §23
```

`src/NativeSpy.Client/` changes are limited to the minimal frozen adapter/connector/session contracts and friend declarations above. `src/NativeSpy.Client.NamedPipes/` changes are limited to the minimal adapter over existing `NamedPipeClientSession`; do not redesign I3 or move existing WinForms protocol semantics into Attach. The manager passes `I3HandshakeTimeout` explicitly; the adapter uses that supplied budget and invents no default. No unrelated refactoring.

### 22.2 A1 prohibited list

A1 MUST NOT implement:

```text
remote DLL injection, remote allocation/write/thread, native bootstrap DLL
hostfxr process-entry mechanics or real native target entry
real resident target coordinator or lifecycle rendezvous wire
moving existing WinForms protocol semantics into NativeSpy.Attach
target-side AllowedUserSid derivation or Agent/Host TFM changes
production GetNamedPipeClientProcessId rendezvous integration
LifecycleOwnerProof target storage/validation
AgentHost changes (including StopAsync)
Agent/Host payload TFM redesign or runtime-specific payload strategy
arbitrary WinForms execution-context discovery
ObjectSpy attach or FlaUI exact-incarnation integration
process-picker UX
WPF, UIA2, writes, invocation, bindings, collection browsing
reconnect, leases, multi-client Agent, automatic elevation
Snoop production concepts/types/errors/files/settings/wire format
service, global broker, driver, debugger, profiler, COM server,
shared-memory control plane, gRPC, persistent RPC, plugin system, DI framework
force reset, ForgetHost, StartAnyway, second Host, or target kill recovery
```

A1 cannot claim a real arbitrary Windows process is attachable.

---

## 23. Deterministic A1 test matrix

Tests use `TaskCompletionSource`, barriers, events, semaphores, and explicit fake gates. Arbitrary sleeps are not the primary race proof.

### Identity, probing, and support

```text
ProcessIdentity value equality and reference-distinct equal DTOs
PID reuse during read-only probe and identity-before/after mismatch rejected
same-handle validated-target seam; PID-only reopen prohibited
exact ProcessIdentity required; PID alone never authorizes
neutral probe => NotRequested + Unknown overall
composition-specific probe and exact winforms ID semantics
fresh composition-specific probe on every admitted AttachAsync path
Attached => AlreadyAttached without another probe
Probing/Bootstrapping/Connecting/Detaching/Settling => AttachInProgress without probe
stale standalone probe cannot authorize entry
UnsupportedComposition => no BeginAttachAsync
Unknown eligibility => no BeginAttachAsync
CleanupUnknown => exactly one reconcile before fresh probe; no Begin before closure
orphan advisory → ReconcileAsync proves Detached => fresh second probe in the
  same attempt; proceed only when that probe is Eligible
orphan advisory becomes live Attached before reconcile acceptance =>
  AlreadyAttached / Reconciliation, PostFailureLifecycle absent, Begin = 0
orphan advisory becomes non-reconcilable transition =>
  AttachInProgress / Reconciliation, PostFailureLifecycle absent, Begin = 0
fresh eligible probe races with pre-Host coordinator Attached =>
  AlreadyAttached / LifecycleAdmission, PostFailureLifecycle absent, Begin = 0
fresh eligible probe races with pre-Host coordinator transition =>
  AttachInProgress / LifecycleAdmission, PostFailureLifecycle absent, Begin = 0
orphan reconciliation completes unresolved => CleanupUnknown / Reconciliation,
  PostFailureLifecycle CleanupUnknown, Begin = 0
authoritative Reconciliation budget expiry => StageTimedOut / Reconciliation,
  PostFailureLifecycle CleanupUnknown; no Begin, second Host, or background polling
orphan reconciliation proves target exit => TargetExited / Reconciliation,
  PostFailureLifecycle TargetExited, Begin = 0
rendezvous authentication failure during reconciliation =>
  RendezvousFailed / Rendezvous, never a lifecycle conflict
orphan reconciliation is mandatory once when its authorization conditions hold
probe timeout typed; caller probe cancellation OCE
insufficient/unbound real runtime evidence => RuntimeSupport Unknown
real probe may report CoreCLR major10 evidence + RuntimeSupport Unknown
Framework CLR uses no weaker evidence standard than CoreCLR
trustworthy evidence of incompatible family/version => KnownUnsupported
fake KnownSupported runtime path exercises manager behavior
no filename/command-line/environment/TFM-only runtime support
AMD64 controller + x64 target + AMD64 Windows => architecture may be supported
x86/ARM64/other target or non-AMD64 Windows => KnownUnsupported when identified
unknown architecture evidence => Unknown
x64-emulated target on ARM64 Windows => KnownUnsupported
Detached attach, same SID + same integrity => security may be KnownSupported,
  subject to all other eligibility dimensions
Detached attach, different SID => KnownUnsupported +
  UnsupportedSecurityContext / Probe + no strategy invocation
Detached attach, same SID/different integrity => KnownUnsupported +
  UnsupportedSecurityContext / Probe + no strategy invocation
token access denied => AccessDenied / Probe + no strategy invocation
security evidence inconclusive without access denial =>
  ProbeInconclusive / Probe + no strategy invocation
```

### Manager lifecycle, identity, and concurrency

```text
valid AttachId ordering; invalid request gets none
fresh distinct AttachId for every valid failed attempt including fail-fast
per AttachAsync: ReconcileAsync 0/1 and BeginAttachAsync 0/1, never retry
same-target attach fail-fast in each required state
Attached => AlreadyAttached; active transient => AttachInProgress
CleanupUnknown => one reconciliation and no Host until closure proof
different-target independence
cross-manager Global mutex contention/fail-fast across Windows sessions
canonical Global gate bytes/name equality; exact case-sensitive start text
restrictive same-user mutex DACL; mutex does not authenticate target
abandoned gate never implies safe entry; only positive Detached proof permits
TargetExited operation table for Probe/Attach/Detach/old session state
same PID with new ProcessIdentity is independent
strict AttachResult/ProbeResult/DetachResult unions
stale A strategy/connector/completion result cannot affect B
successful reattach produces fresh AttachId, Agent SessionId, fake CLR session,
  registries, composition, adapter; old surfaces remain terminal
manager disposal during probe, attach, active session, detach
```

### Cancellation, timeouts, detach, and session surfaces

```text
caller cancellation before BeginAttach => OCE and no entry
cancellation after strategy invocation => no publication + tracked settlement
late bootstrap result after cancellation is cleaned and never published
cancellation/publication linearization: never both session and OCE
probe/native-entry/bootstrap/rendezvous/readiness/I3/detach budgets separated
explicit I3HandshakeTimeout propagated and enforced over connect + Hello
I3 timeout typed separately from caller OCE
DetachCallerWait vs DetachOperation vs CleanupSettlement
shared per-AttachId Detach task/result; later caller joins
repeated Detach returns cached Detached / TargetExited / CleanupUnknown
CleanupUnknown reconciled to Detached updates the same AttachId result
repeated Detach(A) after B starts returns A's cache and cannot stop B
Detached manager record + known historical session is not contract misuse
caller wait timeout not cached; shared success arriving later becomes Detached
Detach timeout never releases gate or immediately admits second Host
Detach/client-loss race has one target stop task and first-cause linearization
Dispose local-only; Dispose-vs-Detach both linearization winners
Dispose-before-detach-claim makes later Detach misuse
Dispose-after-detach-claim still permits repeated join/cache observation
session state precedence: positive TargetExited/Detached facts, then local disposal,
  then manager Detaching/CleanupUnknown, then Active
Dispose followed by later positive TargetExited/Detached evidence updates projection
adapter typed lookup: valid ID returns matching adapter; missing ID, wrong type,
  and inactive state return false; invalid lexical ID throws
previously obtained adapter rejects after terminalization
AttachSession state transitions and projection precedence per §7.3
bounded capability count/ID validation and duplicate rejection
```

### Recovery, security, and diagnostics

```text
locally known CleanupUnknown reconciliation proves Detached / remains unknown /
  target exits; unresolved or timeout returns its existing CleanupUnknown mapping
orphan advisory -> ReconcileAsync proves Detached => fresh second probe in same
  attempt; proceeds only when Eligible
orphan advisory becomes live Attached before reconcile acceptance =>
  AlreadyAttached / Reconciliation; PostFailureLifecycle absent; BeginAttachAsync = 0
orphan advisory becomes non-reconcilable transition =>
  AttachInProgress / Reconciliation; PostFailureLifecycle absent; BeginAttachAsync = 0
orphan reconciliation completes unresolved => CleanupUnknown / Reconciliation,
  PostFailureLifecycle CleanupUnknown; BeginAttachAsync = 0
Reconciliation budget expires => StageTimedOut / Reconciliation,
  PostFailureLifecycle CleanupUnknown; no background polling or second Host
orphan reconciliation proves target exit => TargetExited / Reconciliation,
  PostFailureLifecycle TargetExited; BeginAttachAsync = 0
rendezvous authentication failure during reconciliation =>
  RendezvousFailed / Rendezvous; not AlreadyAttached/AttachInProgress
rendezvous peer verification failure during reconciliation =>
  RendezvousPeerVerificationFailed / Rendezvous; not a lifecycle conflict
no force-reset, polling loop, or second Host
foreign reconciliation rejected while original authenticated client is live
foreign reconciliation requires independent client absence + fresh auth
knowledge/guess of AttachId alone grants no operation
stale A reconcile cannot affect B
rendezvous secret, owner proof, and AttachId are distinct
controller creates rendezvous server; target connects as pipe client
wrong-secret attempt bound, correct-secret identity mismatch retires endpoint
peer PID failure/identity reread mismatch → RendezvousPeerVerificationFailed /
  Rendezvous; fail closed, including during reconciliation
controller ACL identity and target AllowedUserSid source semantics
TargetExited during bootstrap, handshake, and detach
client crash after Host start before I3 leads tracked cleanup
composition missing after I3 => no partial session/downgrade
expected operational failures typed; programmer misuse/invariants throw/fault
diagnostics allowlist/bounds; no exception strings, paths, target text, memory
```

---

## 24. A1 implementation discretion and closure audit

A1 leaves only private implementation details open:

| Potential decision | Class | Frozen by A0? | Safe for A1 implementer? |
|---|---|---|---|
| Private helper/type names and local method decomposition | Private | No semantic effect | Yes |
| Collection used for manager state | Private | Exact equality/state behavior is frozen | Yes |
| Lock/semaphore/TCS choice | Private | Ordering, linearization, and no-queue semantics are frozen | Yes |
| Test helper organization | Private | Required deterministic cases are frozen | Yes |
| Queue vs fail-fast for same-target attach | Public lifecycle | Yes: fail-fast | No choice remains |
| AttachId secrecy/authorization | Security | Yes: correlation only, never authorization | No choice remains |
| Dispose means target detach | Lifecycle | Yes: local cleanup only | No choice remains |
| Unknown probe enters target | Support/security | Yes: never BeginAttach | No choice remains |
| Failed detach admits second Host | Lifecycle/security | Yes: only positive closure/exit permits | No choice remains |
| Foreign orphan reconciliation rule | Security/lifecycle | Yes: live client rejects; independent loss + fresh auth required | No choice remains |
| Exact stronger runtime evidence needed to authorize real entry | Support; C0-owned | A1 returns Unknown when evidence does not prove identity/version | No A1 heuristic choice |
| WinForms ownership | Module ownership | Yes: client/agent composition split | No choice remains |

Adversarial answers:

```text
Two managers begin invasive entry simultaneously? No: canonical gate + coordinator gate.
Can PID reuse retarget an operation? No: exact identity and retained validated object.
Can AttachId authenticate? No; separate rendezvous token/owner proof.
Can a foreign manager stop a live session? No.
Can CleanupUnknown start a second Host before closure proof? No.
Can CleanupUnknown recover? One bounded attach-triggered reconciliation or target exit.
Does local AlreadyAttached/AttachInProgress require an unnecessary probe? No; it is an authoritative fail-fast path.
Can caller cancellation leave untracked attach? No; strategy invocation is tracked and cleanup is manager-owned.
Can caller receive both OCE and AttachSession? No; publication linearization decides.
Can Dispose claim target detach? No.
Can client loss start a duplicate stop? No; one per-generation shared target task.
Can stale A callbacks/results mutate B? No NativeSpy state adoption/publication is generation guarded.
Can neutral/Unknown support invoke target entry? No.
Can runtime support be invented from name/TFM/command line? No; A1 reports family/version evidence separately and returns Unknown when identity/version is not reliably bound; C0 owns the real-entry evidence threshold.
Does runtime support imply payload compatibility? No.
Can different integrity accidentally use the rendezvous? No; equality required before support.
Is I3 deadline explicit and manager-owned? Yes, one end-to-end I3HandshakeTimeout.
Are gate bytes canonical? Yes, exact binary encoding in §10.1.
Are public AttachSession states and adapter lookup frozen? Yes, §7.3–7.4.
Does TargetExited behavior cover all public operations? Yes, §9.5.
Can future WPF/x86/modern runtime support preserve identity semantics? Yes; additions are strategies/support values, not identity rewrites.
Does A1 require an owner-level choice? No; see the tables and exact allowlist above.
```

Remaining owner decisions for I5-A1: none. Later B0/C0/D0/E0/F/G choices remain owned by those iterations. This document is ready for independent closure review, not self-declared closed.

---

## 25. Official Microsoft references

Verified current Microsoft Learn pages relevant to normative platform claims:

- [GetNamedPipeClientProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid)
- [Named Pipe Security and Access Rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)
- [Mandatory Integrity Control](https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control)
- [IsWow64Process2](https://learn.microsoft.com/en-us/windows/win32/api/wow64apiset/nf-wow64apiset-iswow64process2)
- [Process Security and Access Rights](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights)
- [OpenProcess](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocess)
- [OpenProcessToken](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocesstoken)
- [GetTokenInformation](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-gettokeninformation)
- [EnumProcessModulesEx](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-enumprocessmodulesex)
- [GetModuleFileNameEx](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getmodulefilenameexw)
- [CreateMutexW](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createmutexw)
- [WaitForSingleObject (including abandoned mutex result)](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-waitforsingleobject)
- [Write a custom .NET runtime host](https://learn.microsoft.com/en-us/dotnet/core/tutorials/netcore-hosting)
- [Target frameworks in SDK-style projects](https://learn.microsoft.com/en-us/dotnet/standard/frameworks)
- [.NET runtime selection](https://learn.microsoft.com/en-us/dotnet/core/versions/selection)

Platform documentation establishes the API behaviors stated here. NativeSpy's equality policy, runtime evidence threshold, lifecycle rules, and module ownership are architectural/product decisions, not claims made by Microsoft.

---

## 26. Corrective finding disposition map

```text
F-01  pre-entry ProcessIdentity revalidation                 §4.2
F-02  peer-PID primitive ownership and fail-closed policy    §§3.1, 12.3, 20.3
F-03  initial accepted composition set                       §6.4
F-04  concrete authenticated connector handoff               §§8.1–8.2
F-05  authenticated client-session projection and Completion §§8.2–8.3
F-06  bounded adapter contract and terminal behavior         §§7.3–7.4, 13
F-07  independent ObjectSpy/Attach generation guards         §§15–16
F-08  conservative architecture/runtime evidence policy      §§6.1–6.2
F-09  structured runtime version and diagnostic bounds       §§6.2, 17.3
F-10  finite timeout validation                              §11.1
F-11  typed connector failures                                §§8.2, 17
F-12  normalized outer failure taxonomy                       §§17.1–17.3
F-13  exact failure-to-state mapping and PostFailureLifecycle §§7.1, 17.2, 18
```

**End of I5-A0 specification.**
