# NativeSpy Project State

## Current iteration

**I0 — repository bootstrap, framework-neutral correlation contracts, and pure evaluator**

## Implemented

- repository/bootstrap structure and SDK pin;
- framework-neutral correlation DTOs and enums;
- tagged target/source envelopes with constructor validation;
- codec-neutral detached adapter metadata values;
- opaque adapter-owned generation observations;
- detached CLR type identity/reference contracts;
- Client-owned normalized proof and validation requirements;
- pure shared `CorrelationEvaluator`;
- exhaustive evaluator decision-table and architectural regression tests.

## Not implemented

- FlaUI integration or external UIA evidence capture;
- framework-specific proof normalization;
- correlation coordinator/provider selector/candidate joiner;
- real target adapter;
- WinForms or WPF;
- named pipes, agent, attach/bootstrap, or ObjectSpy;
- handle registry/lifetime implementation;
- reflection or member reads;
- serialization codec.

## Next iteration

**I1 — first WinForms/FlaUI walking skeleton**: synthetic WinForms application, one FlaUI UIA3 Button, current-HWND correlation, and `Exact + SameManagedElement`.

## Build status

- SDK: .NET 10.0.401
- TFMs: Protocol `netstandard2.0`; Client/tests `net10.0`
- Build: passing
- Tests: 59 passing, 0 skipped

## Known limitations

- DTOs are logical contracts only; no concrete JSON codec exists.
- Normalized proof assessments are synthetic test inputs until I1 introduces adapters.
- Type identity is detached and structural; no runtime type discovery is implemented.
- The evaluator returns a decision object; a future coordinator will assemble complete public results.

## Architecture deviations

None. The `CorrelationResultDto.Conflicts` name is the explicit implementation spelling of the frozen `alternativesOrConflicts` result slot because candidates are the canonical alternative collection.
