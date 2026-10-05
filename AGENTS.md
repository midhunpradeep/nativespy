# Agent Instructions

These instructions apply to every agent working in the NativeSpy repository.

If `AGENTS.local.md` exists, read it after this file. That file is intentionally untracked and contains working-copy-specific instructions.

## Repository orientation

- Read `README.md`, `PROJECT_STATE.md`, and `CONTRIBUTING.md` before making project changes.
- Keep implementation work within the current iteration and its documented scope.
- Treat the parent `research/` corpus as read-only; do not copy, modify, or index it from this repository.
- Do not commit build output, credentials, tokens, test results, or generated local state.

## Changes and commits

- Do not commit or push unless the user explicitly requests or authorizes it.
- Keep one logical change per commit.
- Use Conventional Commits 1.0.0 with the types and rules in `CONTRIBUTING.md`.
- Before committing, install the repository hook in the current clone:

  ```bash
  git config core.hooksPath .githooks
  ```

- Do not bypass the commit hook with `--no-verify` unless the user explicitly authorizes it.
- Never rewrite or delete published history without explicit user authorization.
- When a commit is requested, run `dotnet restore`, `dotnet build`, `dotnet test`, and `git diff --check`, then inspect the staged diff.

## Git safety

- Push only to the requested remote and branch.
- Use `--force-with-lease`, never an unconditional force push, when an explicitly authorized history rewrite is required.
- Preserve human-authored Git identity; configure agent identity only in local repository configuration when instructed by `AGENTS.local.md`.
