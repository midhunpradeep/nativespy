# NativeSpy Project State

## Current iteration

**I1 — real UIA3/FlaUI to live WinForms Button correlation**

## Implemented

- repository/bootstrap structure and SDK pin;
- framework-neutral correlation DTOs and enums;
- tagged target/source envelopes with constructor validation;
- codec-neutral detached adapter metadata values;
- opaque adapter-owned generation observations;
- detached CLR type identity/reference contracts;
- Client-owned normalized proof and validation requirements;
- pure shared `CorrelationEvaluator`;
- closed-enum validation at Protocol DTO boundaries;
- explicit `CorrelationPolicyDto`; only `Conservative` is accepted by the I1 coordinator;
- positive `ExternalUiaEvidenceDto.ProcessId` validation with no zero sentinel;
- one optional `OperationErrorDto` on each bounded detached operation result;
- asynchronous Client ports and coordinator for `UiaToNative` current-HWND correlation;
- UIA3 FlaUI session/source with numeric HWND, `FromHandle`, and `Compare` evidence;
- synchronous target-side WinForms adapter using `Control.FromHandle`, handle re-resolution, and `ReferenceEquals`;
- x64 synthetic WinForms target and test-only bounded JSON Lines bridge;
- integration proof resolving `NativeSpyTestButton` to the live `System.Windows.Forms.Button` and reading its `Text`;
- Protocol, Client negative-path, and live integration tests.

## Not implemented / deferred

- production transport, named pipes, agent hosting, attach/bootstrap, and process lifetime;
- handle registry, leases, and durable object lifetime;
- WPF, UIA2, ProviderAware, and ObjectSpy;
- reflection-based inspection or general member reads;
- production serialization codec;
- multi-candidate/provider selection beyond the one I1 current-HWND path.

## Next iteration

**I2 — not started.** Any attach, production transport/lifetime, broader framework coverage, ProviderAware behavior, or ObjectSpy work requires a separately confirmed scope.

## Build status

- SDK: .NET 10.0.401
- TFMs: Protocol `netstandard2.0`; Client `net10.0`; UI/target/integration projects `net10.0-windows`;
- Platform: x64 Windows for the live UI path;
- Build: passing with 0 warnings and 0 errors;
- Tests: 82 passing, 0 skipped in the verified environment (21 Protocol, 60 Client, 1 integration).

## Known limitations

- The JSON Lines process bridge and strong object map are test-only; they are not production transport or lifetime infrastructure.
- FlaUI and WinForms live objects are intentionally isolated to their respective projects; Client sees detached facts only.
- I1 supports one retained UIA3 source and one current-HWND candidate path. It does not provide provider selection or heuristic fallback.
- Type identity is detached and structural; the test target's handle table is not a production registry.
- The live integration test requires Windows, x64, modern .NET WinForms, and an interactive desktop/UIA environment.

## Architecture deviations

No scope deviations. The test-only bridge is the deliberate verification seam for I1; no production IPC, attach, registry, reflection, WPF, UIA2, ProviderAware, or ObjectSpy code was added. `CorrelationResultDto.Conflicts` remains the explicit implementation spelling of the frozen `alternativesOrConflicts` result slot because candidates are the canonical alternative collection.
