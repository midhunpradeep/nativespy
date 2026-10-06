# Iteration 4a implementation notes

## Scope

I4a adds the compact ObjectSpy Lite slice on top of the I0–I3 contracts and lifecycle rules. It is intentionally read-only and shallow. The product path is:

```text
NativeSpy.ObjectSpy.App (WPF client UI and overlay)
        ↓
NativeSpy.ObjectSpy (finder/freeze/correlation/inspection coordinator)
        ↓ detached observations and client ports
NativeSpy.FlaUI + NativeSpy.Client.NamedPipes
        ⇄ authenticated Named Pipes
NativeSpy.Agent.Host.WinForms
        ↓
NativeSpy.Agent (weak CLR identity and bounded reflection)
```

The WPF technology belongs only to the client application. I4a does not add WPF target support.

## Protocol and Agent

The four CLR operations are:

- `clr.describeObject` — returns the detached type-bearing object reference;
- `clr.listMembers` — lists public instance fields and readable, non-indexed properties without invoking getters. The response does not expose a total member count. The Agent may enumerate and transiently materialize the bounded eligible member set as required for canonical ordering, quota enforcement, member-set fingerprinting, continuation validation, and determining whether another page exists. No member-set cursor, candidate array, reflection metadata, or equivalent member snapshot is retained between requests;
- `clr.readFieldValues` — reads bounded field batches;
- `clr.readPropertyValue` — explicitly invokes one property getter through the resolved target execution context.

Member IDs are opaque, session-scoped, non-reused identities. Their registry keys are structural and detached; continuation tokens are authenticated, stateless, bounded, and tied to the member-set fingerprint. Handles and type identity remain weak/runtime-boundary aware. A collected object returns `ObjectCollected` and is never rebound.

The `clr.listMembers` response does not expose a total member count.

The Agent may enumerate and transiently materialize the bounded eligible member set as required for canonical ordering, quota enforcement, member-set fingerprinting, continuation validation, and determining whether another page exists.

No member-set cursor, candidate array, reflection metadata, or equivalent member snapshot is retained between requests.

`ClrValueDto` is a closed union. Values include exact integers, IEEE floating-point bits, decimal bits/display, UTF-16 chars and bounded strings, date/time values, enums, `TypeObject`, one-level value types, and weak object references. No arbitrary CLR `ToString`, collection enumeration, automatic getter execution, or implicit object navigation is performed. Reflection-wrapper exceptions expose only detached exception type identity and a wrapper flag.

The hard limits are 128 members/page, 64 fields/batch, 4096 UTF-16 code units, 32 value-type fields, 16 KiB value-type payload, 64 newly registered object references/response, 16384 object records/session, 16384 member records/session, 4096 members/type, and 512-byte continuation tokens. Local string/struct limits produce metadata; frame/JSON limits remain top-level serialization failures.

## Execution context and timeout behavior

The Host resolves a getter target to direct execution, one registered adapter, required-but-unavailable execution, or an explicit conflict. WinForms controls always claim the WinForms adapter, including disposed controls, so a getter never silently falls back to the Host thread. A started getter is not forcibly aborted. The Host budget owns `TargetTimeout`; late work cannot emit a second response and remains accounted for until it finishes.

## FlaUI and ObjectSpy

`FlaUiAutomationSession` owns one UIA3 automation instance and all live UIA objects on a dedicated serialized MTA worker. Preview captures detached process/HWND/bounds/metadata facts and validates candidate `ProcessId` against the attached target PID. Freeze creates an observation and retained source; correlation is the existing conservative UIA-to-WinForms proof path. The session also accepts a thread-safe set of detached excluded HWNDs, checks both candidate and root HWND ancestry, and reports an excluded location as a non-candidate rather than a foreign-process error.

A timeout poisons and quarantines the FlaUI session. Queued UIA work is failed rather than drained after quarantine, and automation objects are not disposed from the wrong thread. The bounded quarantine budget prevents unbounded abandoned workers.

`ObjectSpyCoordinator` owns transactional freeze candidates, separate preview and committed UIA facts, external-selection epochs, a distinct CLR navigation epoch, Exact-only CLR loading, bounded field batches, lazy property reads, member paging, and explicit object-reference back navigation. A replacement candidate remains provisional through detached capture and correlation; acquisition, capture, or external revalidation quarantine before commit does not discard the last healthy CLR graph. Once capture and correlation complete, a valid non-Exact candidate may supersede the old selection with CLR unavailable. Target-side correlation failure remains `Frozen`; only semantically stale external evidence maps to `Stale`.

The WPF app supplies a nonactivating, click-through overlay, polls the physical cursor for finder preview, freezes explicitly, renders detached UIA/correlation facts, reads properties only after the user requests them, and offers reference navigation. The main WPF HWND is registered at `SourceInitialized`; the overlay registers and unregisters its own native HWND as its handle is created and destroyed. The cursor, UIA bounding rectangle, detached geometry, and `SetWindowPos` boundary are explicitly physical desktop pixels, so signed negative monitor coordinates pass through without WPF DIP conversion. Its controlled bootstrap starts the existing synthetic WinForms target; real attach/bootstrap remains outside I4a.

Application shutdown is single-entry from `Closing`: the request is canceled, finder operations and generations are stopped, the coordinator releases its committed selection lifetime, the overlay is closed and unregistered, FlaUI teardown is awaited through its MTA worker-owned boundary, and the controlled target's pipe/process cleanup is awaited with bounded graceful-close and kill fallback. The application then explicitly shuts down WPF; no UI-thread `GetAwaiter().GetResult()` cleanup remains.

## Tests and proof

The implementation includes semantic protocol union/strictness tests, required CLR collection-presence tests, a serialized `clr.listMembers` shape test with no total-cardinality field, canonical scalar and value-type conversion tests, nested malformed CLR-wire rejection tests, malformed-success client/session policy tests, Agent inspection/lifetime tests including arrays/collections, inherited/hidden members, excluded member kinds, and collectible-ALC collection proof, malformed-request session-survival tests, deterministic preview restoration, timeout quarantine, freeze/capture/correlation race tests, MTA normal-failure versus hard-timeout tests, and existing I0–I3 regressions.

`ObjectSpyEndToEndTests.Live_point_preview_and_explicit_window_exclusion_preserve_committed_selection` uses the real `FlaUiAutomationSession.PreviewFromPointAsync` against the displayed WinForms target and a displayed test overlay HWND. It proves target PID/bounds, candidate/root HWND exclusion, non-candidate coordinator behavior, preservation of a committed CLR graph, and restoration of normal target preview after unregistering. `ObjectSpyEndToEndTests.ObjectSpy_freeze_inspects_getters_and_navigates_references` remains the real freeze/correlation/CLR/navigation proof. `SlowGetterIntegrationTests.Getter_deadlines_distinguish_queued_and_started_work_without_duplicate_responses` proves both pre-getter deadline expiry and an already-started blocking getter's single timeout/late completion behavior. `ObjectSpyApplicationShutdownTests.ObjectSpy_executable_closes_without_forced_process_termination` launches the built WPF executable, closes it through the normal OS close path, and checks both owned processes exit. Visual WPF overlay rendering/nonactivation and the complete manual product flow remain manual acceptance items; the test overlay is a live HWND policy proof, not a claim that the WPF overlay visual path is fully automated. The signed-coordinate contract is covered deterministically; manual mixed-DPI hardware validation is unavailable in this environment.

Run from `nativespy/`:

```text
dotnet restore
dotnet build NativeSpy.sln
dotnet test NativeSpy.sln
dotnet run --project src/NativeSpy.ObjectSpy.App/NativeSpy.ObjectSpy.App.csproj
```

## Deferred beyond I4a

Attach/injection/bootstrap discovery, WPF target support, UIA2, ProviderAware correlation, writes, invocation, collection browsing/enumeration, recursive inspection, leases/reacquisition, reconnect/multi-client sessions, and full Object Browser behavior remain deferred. No I4b–I6 behavior is included.
