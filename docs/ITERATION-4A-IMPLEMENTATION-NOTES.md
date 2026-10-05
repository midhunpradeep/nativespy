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
- `clr.listMembers` — lists public instance fields and readable, non-indexed properties without invoking getters;
- `clr.readFieldValues` — reads bounded field batches;
- `clr.readPropertyValue` — explicitly invokes one property getter through the resolved target execution context.

Member IDs are opaque, session-scoped, non-reused identities. Their registry keys are structural and detached; continuation tokens are authenticated, stateless, bounded, and tied to the member-set fingerprint. Handles and type identity remain weak/runtime-boundary aware. A collected object returns `ObjectCollected` and is never rebound.

`ClrValueDto` is a closed union. Values include exact integers, IEEE floating-point bits, decimal bits/display, UTF-16 chars and bounded strings, date/time values, enums, `TypeObject`, one-level value types, and weak object references. No arbitrary CLR `ToString`, collection enumeration, automatic getter execution, or implicit object navigation is performed. Reflection-wrapper exceptions expose only detached exception type identity and a wrapper flag.

The hard limits are 128 members/page, 64 fields/batch, 4096 UTF-16 code units, 32 value-type fields, 16 KiB value-type payload, 64 newly registered object references/response, 16384 object records/session, 16384 member records/session, 4096 members/type, and 512-byte continuation tokens. Local string/struct limits produce metadata; frame/JSON limits remain top-level serialization failures.

## Execution context and timeout behavior

The Host resolves a getter target to direct execution, one registered adapter, required-but-unavailable execution, or an explicit conflict. WinForms controls always claim the WinForms adapter, including disposed controls, so a getter never silently falls back to the Host thread. A started getter is not forcibly aborted. The Host budget owns `TargetTimeout`; late work cannot emit a second response and remains accounted for until it finishes.

## FlaUI and ObjectSpy

`FlaUiAutomationSession` owns one UIA3 automation instance and all live UIA objects on a dedicated serialized MTA worker. Preview captures detached process/HWND/bounds/metadata facts and validates candidate `ProcessId` against the attached target PID. Freeze creates an observation and retained source; correlation is the existing conservative UIA-to-WinForms proof path.

A timeout poisons and quarantines the FlaUI session. Queued UIA work is failed rather than drained after quarantine, and automation objects are not disposed from the wrong thread. The bounded quarantine budget prevents unbounded abandoned workers.

`ObjectSpyCoordinator` owns transactional freeze candidates, separate preview and committed UIA facts, external-selection epochs, a distinct CLR navigation epoch, Exact-only CLR loading, bounded field batches, lazy property reads, member paging, and explicit object-reference back navigation. A replacement candidate remains provisional through detached capture and correlation; acquisition, capture, or external revalidation quarantine before commit does not discard the last healthy CLR graph. Once capture and correlation complete, a valid non-Exact candidate may supersede the old selection with CLR unavailable. Target-side correlation failure remains `Frozen`; only semantically stale external evidence maps to `Stale`.

The WPF app supplies a nonactivating, click-through overlay, polls the physical cursor for finder preview, freezes explicitly, renders detached UIA/correlation facts, reads properties only after the user requests them, and offers reference navigation. Its controlled bootstrap starts the existing synthetic WinForms target; real attach/bootstrap remains outside I4a.

## Tests and proof

The implementation includes semantic protocol union/strictness tests, required CLR collection-presence tests, canonical scalar and value-type conversion tests, nested malformed CLR-wire rejection tests, malformed-success client/session policy tests, Agent inspection/lifetime tests including collectible-ALC collection proof, malformed-request session-survival tests, deterministic preview restoration, timeout quarantine, freeze/capture/correlation race tests, MTA quarantine tests, existing I0–I3 regressions, and a live ObjectSpy integration test proving preview/freeze, Exact correlation, bounded field inspection, explicit getter read, object-reference navigation, and back navigation. The target exposes deterministic synthetic fields/properties solely for this proof while the existing button correlation remains a real `System.Windows.Forms.Button` path.

Run from `nativespy/`:

```text
dotnet restore
dotnet build NativeSpy.sln
dotnet test NativeSpy.sln
dotnet run --project src/NativeSpy.ObjectSpy.App/NativeSpy.ObjectSpy.App.csproj
```

## Deferred beyond I4a

Attach/injection/bootstrap discovery, WPF target support, UIA2, ProviderAware correlation, writes, invocation, collection browsing/enumeration, recursive inspection, leases/reacquisition, reconnect/multi-client sessions, and full Object Browser behavior remain deferred. No I4b–I6 behavior is included.
