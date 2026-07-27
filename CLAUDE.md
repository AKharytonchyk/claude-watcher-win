# CLAUDE.md

Agent guidance for this repo is canonical in **[AGENTS.md](AGENTS.md)** — read
it first (build/run/verify, source layout, conventions, gotchas). The
non-negotiable principles are in **[CONSTITUTION.md](CONSTITUTION.md)**. The
session-file data contract is in **[SCHEMA.md](SCHEMA.md)**.

Quick reminders:
- **Spec-driven** via GitHub Spec Kit: use `/speckit-specify` → `/speckit-plan` →
  `/speckit-tasks` → `/speckit-implement` for real features; the canonical
  constitution is `.specify/memory/constitution.md` (`/speckit-constitution`).
- WinUI 3 / C# / .NET 10. Build & verify on **Windows** (`dotnet build`, then open
  the tray flyout). The `Core/` library is the only piece that builds/tests
  cross-platform.
- Keep `Core/` free of WinUI/Win32 so it stays unit-testable.
- Keep it lean; never steal focus; local-first, read-only. Don't `git add -A`.
- Two things that bite: WSL PIDs aren't Windows PIDs (liveness), and
  `FileSystemWatcher` doesn't fire over `\\wsl$` (poll instead).
