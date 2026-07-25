# 0001 — Windows tray MVP (native + WSL)

> **Note:** this is the original design doc, written before GitHub Spec Kit was
> adopted in this repo. Formal feature specs now go through the Spec Kit workflow
> (`/speckit-specify` → `/speckit-plan` → `/speckit-tasks`) under `specs/<feature>/`.
> This doc stays as the architectural north star for the MVP.


- **Status:** in progress — **Core complete + tested** (39 unit tests). Phases
  1–3 implemented and now **build green on CI** (`windows-latest`): WSL discovery,
  native watcher + WSL polling, tray render, acrylic flyout, Win32 window focus.
  **Runtime unverified** — pending a real Windows desktop (launch the tray, open
  the flyout, confirm native + WSL sessions appear and update). `PrChecker`
  remains a stub.
- **Owner:** —
- **Gate:** Phase 2 (WSL) must prove that (a) a distro's `~/.claude/sessions` is
  readable from Windows via `\\wsl$` and (b) WSL-session liveness is derivable
  before the polling watcher and origin tagging are built out.

## Problem

Claude Watcher exists for macOS as a menu-bar app. Windows users run Claude Code
in two places — natively in PowerShell/cmd, and inside WSL distros — and have no
ambient "who needs me" signal. We want a native Windows tray app that aggregates
both, reusing the macOS app's proven data model and state logic.

The macOS app cleanly separates a pure-logic **brain** (session schema, state
classification, context inference, transcript mining) from a platform surface
(menu bar, FSEvents, `ps`/`kill`, `osascript`). The brain ports; the surface is
rewritten. See [SCHEMA.md](../SCHEMA.md) for the shared data contract.

## Goals

- A tray icon showing the **dominant-urgency** state (red > yellow > green) with a
  per-state breakdown in the tooltip.
- A Fluent (acrylic/mica) **flyout** on click: header + filter chips + a row per
  session (dot, name, state, last intent, branch, ctx%), matching the macOS
  popover's information design.
- Aggregate sessions from the **native** root and **every WSL distro**, each row
  tagged by origin (PowerShell / Ubuntu / Debian / …).
- Local-first, read-only, no daemon, never steals focus (Constitution §1, §2).

## Non-goals

- Tab-level "jump to terminal" (no public API — window-level focus only).
- Toasts on by default (opt-in later, honoring §2).
- Multi-agent (Codex etc.) — a separate spec if ever, per §3.
- Cost history, dashboards, analytics.

## Design

### Layering (enforced boundary)

```
Core/      pure C#, no WinUI/Win32 — unit-testable, honest to SCHEMA.md
Platform/  Windows-specific: watching, liveness, focus, WSL, tray render
UI/        WinUI 3 XAML flyout
App        tray host wiring Platform → Core → UI
```

### Roots — the crux of the port

The macOS `AgentAdapter.watchPaths` seam generalizes to a set of **watch roots**.
One Claude source enumerates sessions from several roots:

```csharp
interface IWatchRoot {
    string   Id       { get; }   // "native" | "wsl:Ubuntu"
    string   Origin   { get; }   // display label: "PowerShell" | "Ubuntu"
    string   SessionsDir { get; }// filesystem path (native or \\wsl$\…)
    bool     IsWsl    { get; }
    string?  Distro   { get; }   // WSL distro name, when IsWsl
    bool     IsAlive(int pid);   // native: Win32; WSL: `wsl.exe kill -0`/mtime
}
```

- **Native root:** `%USERPROFILE%\.claude\sessions`, Windows-PID liveness.
- **WSL roots:** `wsl.exe -l -q` → distros; per distro resolve `$HOME`
  (`wsl.exe -d <d> -- printf %s "$HOME"`), watch `\\wsl$\<d>\home\<user>\.claude\sessions`.
  Override the distro set with `CWATCH_WSL`.

The origin tag replaces the macOS host-glyph concept (iTerm/VS Code/Terminal) with
something more useful on Windows: *where the agent is rooted*.

### Watching

- Native root → `FileSystemWatcher` (event-driven).
- WSL roots → **poll** mtimes every ~1–2 s. `FileSystemWatcher` does **not** fire
  reliably over the `\\wsl$` 9P share; do not rely on it.
- Debounce/coalesce → a single "sessions changed" event feeding the model.

### Tray + flyout

- Tray glyph drawn at runtime (`TrayIconRenderer`): a filled dot in the dominant
  state color; tooltip = `summaryText` ("1 needs you · 2 working · 3 idle").
- Left-click → toggle the flyout, a borderless acrylic window anchored above the
  tray (like the Windows volume/network flyouts). Right-click → context menu
  (filters, quit).
- Ambient alert = glyph color change. No toast in the MVP.

### Focus

- `TerminalFocus`: walk the process tree (`CreateToolhelp32Snapshot`) to the
  owning top-level window, `SetForegroundWindow`. Window-level only; WSL sessions
  may not resolve to a window at all — degrade gracefully (no-op, no error).

## Phases

1. **Read-only native MVP.** Core port + `NativeRoot` + `FileSystemWatcher` + tray
   dot + tooltip + a static flyout list. Proves the brain + tray shell.
2. **WSL roots** (gate). Distro enumeration, `$HOME` resolution, WSL liveness,
   polling watcher, origin tags in rows.
3. **Enrichment.** Transcript intent + ctx%, `PrChecker` via `gh` (offline-able),
   window focus on row click, acrylic styling, light/dark + accent.
4. **Polish / optional.** Opt-in toast on `→waiting`, taskbar overlay badge if a
   window is ever added, first-run "pin the tray icon" nudge.

## Acceptance

- With only native sessions, the tray dot matches the dominant state and the
  flyout lists them; no window steals focus on launch.
- With WSL sessions present, they appear tagged by distro, with correct liveness
  and state.
- `CWATCH_OFFLINE=1` removes all network activity; the app is otherwise fully
  functional. Nothing is ever written under `~/.claude`.

## Risks / open questions

- **WSL `$HOME`/user discovery** across distros (root vs. user, custom homes).
- **`\\wsl$` availability** when a distro is stopped — enumerate lazily; a stopped
  distro should be silently skipped, not an error.
- **Polling cost** over 9P with many distros — cap frequency; back off when idle.
- **Unpackaged WinUI 3 bootstrap** quirks (runtime provisioning on clean machines).
- **Tray icon overflow** on Win11 (hidden by default) — needs a first-run nudge.
