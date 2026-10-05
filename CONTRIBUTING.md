# Contributing to NativeSpy

This document records the public contribution and commit conventions for NativeSpy.

## Repository

- GitHub: <https://github.com/midhunpradeep/nativespy>
- Default branch: `main`
- Remote: `origin`
- Research materials live in the parent repository and must not be copied or modified from this implementation repository.

## Conventional Commits

Commit messages follow [Conventional Commits 1.0.0](https://www.conventionalcommits.org/en/v1.0.0/):

```text
<type>[optional scope][!]: <description>

[optional body]

[optional footer(s)]
```

Preferred types:

- `feat` — user-visible or architectural capability;
- `fix` — bug fix;
- `test` — tests or test infrastructure;
- `docs` — documentation only;
- `refactor` — behavior-preserving code restructuring;
- `build` — project or dependency build changes;
- `ci` — continuous-integration changes;
- `chore` — maintenance that does not fit another type.

Examples:

```text
feat(i0): bootstrap NativeSpy correlation foundation

Add framework-neutral Protocol DTOs and contract validation.
Add the pure Client-owned correlation certainty evaluator.
Add exhaustive decision-table and architecture regression tests.
Document I0 structure, invariants, and deliberate deferrals.
```

```text
fix(evaluator): reject failed proof in HighConfidence decisions

test(evaluator): cover missing lifecycle validation

docs(repo): explain contribution conventions
```

Use `!` for a breaking change, or include a footer such as:

```text
BREAKING CHANGE: rename the public correlation result field
```

Keep the subject imperative, concise, and specific. Use the body to explain why when the subject is not enough.

## Local commit hook

This repository includes a tracked `commit-msg` hook. Install it once per clone:

```bash
git config core.hooksPath .githooks
```

The hook validates normal commit subjects, permits Git-generated merge and revert subjects, and rejects `fixup!` and `squash!` subjects. It checks the subject only; Conventional Commit bodies and footers remain available. Local hooks can be bypassed with `--no-verify`, but agents and contributors should not bypass them.

## Validation before commit

From the repository root:

```bash
dotnet restore
dotnet build
dotnet test
git diff --check
git status --short
```

Do not commit `bin/`, `obj/`, test results, credentials, tokens, or generated local state. Build warnings are treated as errors by the repository configuration.

For NativeSpy implementation changes, also update `PROJECT_STATE.md` and relevant notes when the project state, iteration boundary, or known limitations change.

## Scope guardrails

Do not start a future iteration merely because it would be convenient. I1 deliberately stops at the test-only UIA3/FlaUI-to-WinForms walking skeleton. Production transport, attach/bootstrap, handle lifetime/registry, WPF, UIA2, ProviderAware behavior, serialization, reflection/member inspection, and ObjectSpy remain out of scope until separately confirmed.
