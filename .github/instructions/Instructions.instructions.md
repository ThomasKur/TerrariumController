---
applyTo: '**'
---
Provide project context and coding guidelines that AI should follow when generating code, answering questions, or reviewing changes.

Project guardrails:
- Do not create markdown files for documentation, notes, and summaries unless explicitly requested.
- Prefer ASCII-only content unless the existing file already uses non-ASCII characters.
- Be careful with GPIO pin mappings; never change relay-to-GPIO assignments without explicit user approval.
- Assume Raspberry Pi OS environment; avoid OS-specific commands that are not portable to Raspberry Pi unless requested.
- When editing UI behavior, preserve the 1°C hysteresis and humidity lockout logic descriptions.
- Keep README and user-facing text consistent with the latest committed system behavior (no promises of unimplemented features).
- Do not add credentials, secrets, or device-specific tokens to examples.
- When finishing, run:
  1. Run the build script first:
    - From repo root, execute `pwsh -File PiSource/install/build-for-pi.ps1`.
    - If build fails, stop and report the failure clearly. Do not create commits.
  2. Inspect git changes:
    - Include tracked, modified, deleted, and untracked files. Also include cases where TerrariumController.dll is updated.
    - Stage with `git add -A`.
  3. Create commit(s):
    - If changes belong to one concern, create one commit.
    - If changes contain different concerns (for example docs + code fix + tests), create multiple commits by logical grouping.
    - Use concise Conventional Commit messages (for example `feat: ...`, `fix: ...`, `docs: ...`, `chore: ...`, `test: ...`).
    - Do not amend existing commits unless the user explicitly requests it.
  4. After all commits are created, ask the user:
    - "Commits are ready. Should I push them to origin now?"
    - Do not push unless the user explicitly says yes.
  5. If user approves push:
    - Push current branch to origin.
    - Report push result.

Safety and policy:
- Never run destructive git commands (no hard reset, no checkout discard).
- Never revert unrelated local changes.
- Keep commit messages aligned with repository conventions.