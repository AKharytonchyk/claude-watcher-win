# Claude Watcher (Windows) Constitution

The non-negotiable principles for Claude Watcher on Windows. Any change — by a
human or an agent — must uphold these. When a request conflicts with one, stop
and surface it. This reproduces the macOS app's constitution for **Windows +
WSL**: the platform differs, the principles do not. This file is canonical and is
read by the Spec Kit workflow's Constitution Check; the root `CONSTITUTION.md`
points here.

## Core Principles

### I. Privacy-first
Local-only. No telemetry, no analytics, no network of our own. Read the user's
`~/.claude` and repos **read-only**; write nothing to disk. Read-only extends
**across the WSL boundary** — reading each distro's
`\\wsl$\<distro>\home\<user>\.claude` and its repos is still read-only, and we
never write into a distro. The single outbound path (the `gh` PR lookup — native
or `wsl.exe -- gh`) must stay optional and disable-able (`CWATCH_OFFLINE`). See
[PRIVACY.md](../../PRIVACY.md).

### II. Never steal focus
This is an ambient status object, not a foreground app. Never call
`SetForegroundWindow` we didn't trigger, never open a window/flyout unprompted,
never use a modal. Ambient alerts change the tray glyph's color; interrupting
alerts (toasts) are opt-in and off by default. Any focus change must be
user-initiated (they clicked a row or a link) — and focusing a terminal is
window-level and best-effort, never a grab.

### III. Lean & Claude-focused
One WinUI 3 app, no daemon, no bundled service, minimal dependencies; prefer
unpackaged distribution over installer ceremony. Don't leave a resident helper
running — query WSL on demand (`wsl.exe`), never a persistent shim inside a
distro. Resist scope creep: no web dashboard, no analytics stack, no background
service. Broad multi-agent support lands only through a spec, never ad-hoc.

### IV. Native, not templated
Follow the Windows Fluent design language. System font (Segoe UI Variable),
system materials (Mica/Acrylic), theme resources that adapt to light/dark and to
the user's accent color. Defer to content; be restrained with color (one color
system = the state dot); take away until only the signal remains. It should feel
like it shipped with Windows, not like a cross-platform port.

### V. Verify before shipping (NON-NEGOTIABLE)
Drive the real thing — build on Windows and open the flyout — before declaring a
UI change done. Compiling is not verifying; XAML binding errors surface at
runtime. Verify with **both** a native (PowerShell) session and a **WSL** session
present, since liveness and watching differ across that boundary. `Core` is the
cross-platform, unit-testable part; the tray, flyout, and WSL paths are
Windows-only. Report outcomes honestly (what was tested, what was skipped, on
which Windows version).

## Additional Constraints (platform & architecture)

- **Stack:** WinUI 3 (Windows App SDK) + C# / .NET 8, unpackaged.
- **Layering:** `ClaudeWatcher.Core` (pure logic) must not reference WinUI or
  Win32 — only the BCL — so it stays unit-testable. Anything that spawns
  `wsl.exe` or P/Invokes Win32 lives in `ClaudeWatcher/Platform/`.
- **Data contract:** the session-file schema in [SCHEMA.md](../../SCHEMA.md) is
  authored by Claude Code (read-only for us) and is shared with the macOS app;
  change it there and in both readers together.
- **WSL realities:** a WSL session's `pid` is a Linux pid (never feed it to Win32
  — use `wsl.exe -d <distro> -- kill -0`); `FileSystemWatcher` does not fire over
  the `\\wsl$` 9P share (poll instead).

## Development Workflow (spec-driven)

- This repo uses **GitHub Spec Kit**. Real subsystems flow through
  `/speckit-specify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-implement`,
  producing feature specs under `specs/<feature>/`. Governance should never cost
  more than the work: trivial changes ship directly.
- **Small stays small.** Right-size the process; only genuine subsystems get a
  spec.
- Runtime/agent guidance (build/run/verify, conventions, gotchas) lives in
  [AGENTS.md](../../AGENTS.md).

## Governance

This constitution supersedes other practices. Every implementation plan runs a
**Constitution Check** against these principles; complexity that violates a
principle must be justified in the plan or the approach changed. Amendments are
documented here with a bumped version and an updated amendment date. PRs and
reviews verify compliance.

**Version**: 1.0.0 | **Ratified**: 2026-07-25 | **Last Amended**: 2026-07-25
