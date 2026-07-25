# Claude Watcher for Windows

A tiny **system-tray** app that shows which running Claude Code agent needs you —
at a glance, from anywhere on your desktop. The Windows sibling of
[claude-watcher](https://github.com/AKharytonchyk/claude-watcher) (macOS).

- 🔴 **needs you** · 🟡 **working** · 🟢 **idle** — a traffic-light dot in the
  notification area, full breakdown in the tooltip and a Fluent flyout.
- Watches **both** Claude Code running natively (PowerShell/cmd) **and** inside
  **WSL** distros, aggregated into one list and tagged by origin.
- **Local-first, read-only, no daemon, never steals focus.** Same principles as
  the macOS app — see [CONSTITUTION.md](CONSTITUTION.md) and [PRIVACY.md](PRIVACY.md).

> **Status: pre-alpha.** The pure-logic **Core is complete and tested** (39 unit
> tests). The whole solution — Core **and** the WinUI app — now **builds green on
> CI** (`windows-latest`). What's left is **runtime** verification: the tray glyph,
> flyout, WSL discovery, watcher, and window focus compile but haven't been run on
> a real Windows desktop yet. Not a shippable app yet. See
> [`specs/0001-windows-tray-mvp.md`](specs/0001-windows-tray-mvp.md) for the plan
> and [AGENTS.md](AGENTS.md) for the per-file `tested` vs. `unverified` status.

## Stack

WinUI 3 (Windows App SDK) + C# / .NET 8 — Fluent design, mica/acrylic, automatic
light/dark. Tray icon via [`H.NotifyIcon`](https://github.com/HavenDV/H.NotifyIcon).
Unpackaged (no MSIX required) to keep distribution lean.

## Build (on Windows)

```powershell
dotnet build src/ClaudeWatcher/ClaudeWatcher.csproj -c Debug
dotnet run   --project src/ClaudeWatcher/ClaudeWatcher.csproj
```

Requires the .NET 8 SDK and the Windows App SDK workload. See
[AGENTS.md](AGENTS.md) for the full build/run/verify loop and prerequisites.

## Data source

Claude Code writes a small JSON file per live session under `~/.claude/sessions`.
That schema — the contract this app reads — is documented in
[SCHEMA.md](SCHEMA.md) and is identical across macOS, Windows, and WSL. Keep it in
sync with the [macOS repo](https://github.com/AKharytonchyk/claude-watcher).

## Related

- **macOS:** [AKharytonchyk/claude-watcher](https://github.com/AKharytonchyk/claude-watcher)
  — the original menu-bar app this is a sibling of. Same principles
  ([CONSTITUTION](CONSTITUTION.md)) and the same read-only session-file contract
  ([SCHEMA](SCHEMA.md)); the pure-logic core here is a port of its Swift model.

> The reciprocal link (macOS repo → this one) will be added once the Windows app
> is confirmed running on a real desktop.

## License

MIT © 2026 Artsiom Kharytonchyk
