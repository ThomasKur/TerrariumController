---
applyTo: '**'
---
Provide project context and coding guidelines that AI should follow when generating code, answering questions, or reviewing changes.

Project guardrails:
- Keep documentation concise; link to sources instead of duplicating long instructions.
- Prefer ASCII-only content unless the existing file already uses non-ASCII characters.
- Be careful with GPIO pin mappings; never change relay-to-GPIO assignments without explicit user approval.
- Assume Raspberry Pi OS environment; avoid OS-specific commands that are not portable to Raspberry Pi unless requested.
- When editing UI behavior, preserve the 1°C hysteresis and humidity lockout logic descriptions.
- Keep README and user-facing text consistent with current system behavior (no promises of unimplemented features).
- Do not add credentials, secrets, or device-specific tokens to examples.