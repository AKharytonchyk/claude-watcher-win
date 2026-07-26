# AGENTS.md

Canonical guidance for AI coding agents (and humans) working on **Claude Watcher
for Windows**. Read this first. The non-negotiable principles live in the
constitution at [`.specify/memory/constitution.md`](.specify/memory/constitution.md)
(the root [CONSTITUTION.md](CONSTITUTION.md) points there). The session-file data
contract lives in [SCHEMA.md](SCHEMA.md).

This repo is **spec-driven** via [GitHub Spec Kit](https://github.com/github/spec-kit):
real subsystems go through `/speckit-specify` → `/speckit-plan` → `/speckit-tasks`
→ `/speckit-implement` (skills under `.claude/skills/`, infra under `.specify/`),
producing feature specs in `specs/<feature>/`. Amend the constitution with
`/speckit-constitution`. Trivial changes still ship directly — governance never
costs more than the work.

## What this is

A tiny Windows **system-tray** app (WinUI 3 / C# / .NET 10) that shows which
running Claude Code agent needs you, read from `~/.claude/sessions`. It aggregates
Claude Code running **natively** (PowerShell/cmd) and inside **WSL** distros. One
app, no daemon, local-first, read-only.

Sibling of the macOS app ([claude-watcher](https://github.com/AKharytonchyk/claude-watcher)).
The pure-logic **core** is a near-direct port of the Swift model; the platform
and UI layers are Windows-native rewrites.

## Build · run · verify  (Windows only)

```powershell
dotnet build ClaudeWatcher.sln -c Debug                        # compile
dotnet test  tests/ClaudeWatcher.Core.Tests                    # core unit tests
# then launch the tray host (see the output-path note below):
src/ClaudeWatcher/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/ClaudeWatcher.exe
```

Prerequisites: the **.NET 10 SDK**, the **Windows App SDK** (carried in-app, see
below), and Windows 10 19041+ / Windows 11. The tray/flyout cannot be verified on macOS or Linux — the
core library and its tests are the only pieces that build cross-platform.

- **Build the solution, not the project.** `ClaudeWatcher.sln` sets `Platform=x64`,
  so its output lands in `bin/x64/Debug/…/win-x64/`. Building the `.csproj`
  directly writes to `bin/Debug/…/win-x64/` instead — build one path and launch the
  other and you will test a stale binary and chase ghosts.
- **The Windows App SDK is self-contained** (`WindowsAppSDKSelfContained`). An
  unpackaged app otherwise needs a matching Windows App Runtime *plus* a registered
  DDLM; a machine carrying only the framework package (they arrive as dependencies
  of other apps) exits instantly with `0x80670016`, "Package dependency criteria
  could not be resolved", before `OnLaunched` runs.

- **Verify UI changes by driving the real UI.** XAML binding errors surface at
  runtime, not compile time. Before shipping a UI change, launch and open the
  flyout on a real Windows session. Confirm the tray glyph reflects the dominant
  state and the flyout lists sessions from both native and WSL roots.

## Source layout

Two projects behind one solution (`ClaudeWatcher.sln`):

```
src/ClaudeWatcher.Core/   # pure logic — net10.0, NO WinUI/Win32, unit-testable everywhere
src/ClaudeWatcher/        # WinUI 3 app — net10.0-windows, references Core
tests/ClaudeWatcher.Core.Tests/   # xUnit over Core (builds/runs off-Windows too)
```

**Status legend:** `tested` = builds + unit-tested (macOS/CI) · `verified` =
exercised on a real Windows 11 desktop with live native **and** WSL sessions ·
`unverified` = compiles on `windows-latest` CI but not yet run on a real desktop.

| File | Purpose | Status |
|------|---------|--------|
| `ClaudeWatcher.Core/Session.cs`          | `~/.claude/sessions/<pid>.json` record + helpers | tested |
| `ClaudeWatcher.Core/AgentState.cs`       | State enum, urgency order, state→color token | tested |
| `ClaudeWatcher.Core/StatusClassifier.cs` | `Classify`, `WaitingReason`, counts, summary text | tested |
| `ClaudeWatcher.Core/ContextWindow.cs`    | Context-window inference, model name, token formatting | tested |
| `ClaudeWatcher.Core/AgentSession.cs`     | Normalized session + `IAgentSource`/`IWatchRoot` seams (per macOS spec 0001) | tested (types) |
| `ClaudeWatcher.Core/ClaudeSource.cs`     | Reads `<root>/*.json` across roots → live, normalized `AgentSession`s | tested |
| `ClaudeWatcher.Core/TranscriptReader.cs` | Last intent/said/token usage from the `.jsonl` transcript — tail read + carry-forward + rewrite detection | tested |
| `ClaudeWatcher.Core/GitBranch.cs`        | Current branch from `.git/HEAD` | tested |
| `ClaudeWatcher.Core/AgentView.cs` + `FleetBuilder.cs` | Enrich sessions → display-ready views + counts | tested |
| `ClaudeWatcher.Core/DotGlyph.cs`         | Tray dot as a pure BGRA pixel buffer | tested |
| `ClaudeWatcher.Core/Roots/*`             | `IWatchRoot` seam (interface only; impls live in Platform) | tested (types) |
| `ClaudeWatcher/Platform/Wsl.cs`          | `wsl.exe` distro list / `$HOME` / `\\wsl$` + `/mnt/<drive>` path translation | verified |
| `ClaudeWatcher/Platform/WatchRoots.cs`   | Native + per-WSL-distro root discovery (the port's crux) | verified |
| `ClaudeWatcher/Platform/SessionWatcher.cs` | FileSystemWatcher (native) + polling (WSL 9P) + debounce | verified |
| `ClaudeWatcher/Platform/ProcessLiveness.cs`| Windows PID liveness; WSL liveness via `wsl.exe kill -0` | verified |
| `ClaudeWatcher/Platform/TerminalFocus.cs`  | Focus the hosting window (window-level, not tab) via Win32 | unverified |
| `ClaudeWatcher/Platform/TrayIconRenderer.cs`| `DotGlyph` bytes → GDI HICON for the shell | verified |
| `ClaudeWatcher/Platform/PrChecker.cs`      | Optional open-PR lookup via `gh` (gated by `CWATCH_OFFLINE`) | **stub** |
| `ClaudeWatcher/UI/FleetViewModel.cs`       | Observable snapshot the flyout binds to | verified |
| `ClaudeWatcher/UI/Converters.cs`           | State→brush, ctx%→text, bool→visibility | verified |
| `ClaudeWatcher/UI/FlyoutWindow.xaml(.cs)`  | Acrylic flyout: header, rows, footer | verified |
| `ClaudeWatcher/App.xaml.cs`                | Tray host, watcher wiring, off-thread refresh → dispatcher | verified |

Keep the **Core ⇄ app** boundary clean: `ClaudeWatcher.Core` must not reference
WinUI or Win32 (only the BCL) so it stays unit-testable and honest to
[SCHEMA.md](SCHEMA.md). Anything that spawns `wsl.exe` or P/Invokes Win32 lives in
`ClaudeWatcher/Platform/`.

## Data-source facts

See [SCHEMA.md](SCHEMA.md) for the full contract. Windows-specific:

- **Native (PowerShell/cmd):** `%USERPROFILE%\.claude\sessions\<pid>.json`. The
  `pid` is a **Windows** PID — liveness via `Process.GetProcessById` / `OpenProcess`.
- **WSL:** each distro writes to its Linux home, reachable from Windows at
  `\\wsl$\<Distro>\home\<user>\.claude\sessions`. Enumerate distros with
  `wsl.exe -l -q`; resolve `$HOME` per distro. The `pid` is a **Linux** PID in the
  distro's namespace — Windows APIs can't see it.
- Transcripts: `~/.claude/projects/<cwd-with-non-alnum→->/<sessionId>.jsonl`
  (same encoding rule as macOS).

## Gotchas (design around these up front)

- **WSL liveness ≠ Windows liveness.** A WSL session's `pid` is a Linux PID.
  Check via `wsl.exe -d <distro> -- kill -0 <pid>` (batch one call per distro) or
  fall back to session-file mtime staleness. Never feed a WSL PID to a Win32 API.
- **FileSystemWatcher does not fire reliably over the `\\wsl$` 9P share.** Use
  event-driven watching for the native root only; **poll** mtimes (~1–2 s) for
  WSL roots.
- **"Jump to terminal" is window-level, not tab-level.** There is no public API
  to select a specific Windows Terminal tab/pane by tty (no iTerm/AppleScript
  analog). Walk the process tree to the owning window and `SetForegroundWindow`;
  be honest that it focuses the window, not the exact tab.
- **The tray needs a real HICON.** Assigning a `WriteableBitmap` to
  `TaskbarIcon.IconSource` compiles, registers the icon, and shows the tooltip — but
  the tray slot renders **blank**; H.NotifyIcon only resolves image sources it can
  load from a URI. Build the icon with GDI (`CreateDIBSection` +
  `CreateIconIndirect`) and hand the handle to `TrayIcon.UpdateIcon`, premultiplying
  alpha (the shell composites with `AC_SRC_ALPHA`).
- **Set `NoLeftClickDelay`.** By default H.NotifyIcon defers `LeftClickCommand`
  through a double-click disambiguation timer that fires on a **timer thread**;
  creating a WinUI `Window` there throws into a void, so the flyout silently never
  appears. Marshal to the dispatcher too — a swallowed exception here is invisible.
- **Never read a whole transcript.** They reach tens of MB, `Detail()` runs for every
  live session on every refresh, and an active session's mtime changes constantly so
  no cache saves you: reading it all cost ~181 ms per refresh on a 70 MB file vs
  ~7 ms for the 1 MiB tail. Because the tail can miss a field, values are carried
  forward from the previous parse — which means you must also detect a **rewrite**
  (`--rewind`, the 2.1.208 transcript prune) or you serve stale data forever. Length
  shrink alone doesn't catch it; there's a fingerprint of the bytes ending at the last
  read offset.
- **A WSL cwd is often on a Windows drive.** `/mnt/c/...` must map back to `C:\...`;
  reaching it as `\\wsl$\<Distro>\mnt\c\...` is a needless 9P round trip that fails
  often enough to blank out git/branch lookups.
- **The tray glyph is image + tooltip only** — it can't render rich text like the
  macOS menu bar's `● 1 ● 2`. Draw a single dominant-urgency dot; put the full
  per-state breakdown in the tooltip and the flyout.
- **Never `git add -A`.** Stage explicit paths. `.claude/skills/` (the Spec Kit
  workflow) and `.specify/` **are** committed; `.claude/settings.local.json` and
  `.entire/` are local-only (gitignored).
- End commit messages with the `Co-Authored-By` trailer.

## Environment variables

- `CWATCH_OFFLINE` — disable the only network path (the `gh` PR lookup).
- `CWATCH_CONTEXT_WINDOW` — force the context-gauge window (e.g. `1m` or `1000000`).
- `CWATCH_WSL` — override which WSL distros are scanned (comma-separated; default: all).

## How to work here

- **Match the surrounding style** — naming (`PascalCase` types/methods,
  `camelCase` locals), file-scoped namespaces, nullable enabled, comment density.
- **Simplicity first** — minimum code that solves it; no speculative abstractions.
- **Surface tradeoffs, don't hide confusion** — state assumptions; if unclear, ask.
- **Small changes stay lightweight**; only real subsystems get a `specs/` doc.

## Out of scope (by design — see the Constitution)

Daemon/background service, web dashboard, telemetry/analytics, any UI that steals
focus, and broad multi-agent support (that only lands via a `specs/` doc, not
ad-hoc).
