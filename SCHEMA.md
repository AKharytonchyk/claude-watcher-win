# SCHEMA.md — the Claude Code session-file contract

This is the data contract Claude Watcher reads. It is written by Claude Code, not
by us, and is **identical across macOS, Windows, and WSL**. This file is the
shared source of truth between the macOS and Windows apps — if Claude Code's
format changes, update this file and both readers together.

We read this data **read-only**. We never write to `~/.claude`.

## Locations

| Environment | Sessions dir | `pid` namespace |
|-------------|--------------|-----------------|
| macOS / Linux | `~/.claude/sessions/` | OS PID |
| Windows native (PowerShell/cmd) | `%USERPROFILE%\.claude\sessions\` | Windows PID |
| WSL distro | `\\wsl$\<Distro>\home\<user>\.claude\sessions\` (from Windows) | Linux PID (distro namespace) |

## Session file — `~/.claude/sessions/<pid>.json`

One file per live Claude Code process, named by its `pid`. Fields (all besides
`pid`/`sessionId`/`cwd` are best-effort / may be absent):

| Field | Type | Notes |
|-------|------|-------|
| `pid` | int | Process id (see namespace column above). |
| `sessionId` | string | Stable session id; also the transcript filename. |
| `cwd` | string | Working directory. POSIX path in WSL, Windows path natively. |
| `name` | string? | Friendly session name, if set. |
| `version` | string? | Claude Code version. |
| `status` | string? | `"busy"` \| `"idle"` \| `"waiting"`. |
| `waitingFor` | string? | Why it's blocked, when `status == "waiting"` (see below). |
| `kind` | string? | e.g. `"interactive"`. |
| `startedAt` | number? | Epoch **milliseconds**. |
| `updatedAt` | number? | Epoch milliseconds. |
| `statusUpdatedAt` | number? | Epoch milliseconds — when `status` last changed. |

### `status` → traffic light

| `status` | State | Color |
|----------|-------|-------|
| `waiting` | needs you | 🔴 red |
| `busy` | working | 🟡 yellow |
| anything else (`idle`, `shell`, missing) | idle | 🟢 green |

`waiting` is the reliable "blocked on the user" signal.

### `waitingFor` phrasings

Claude Code emits one of: `permission prompt`, `input needed`, `dialog open`,
`worker request`, `sandbox request`. Note that interactive *questions* and tool
*approvals* both surface as `permission prompt`, so phrase it neutrally
("awaiting your response") — it always means "you need to respond".

## Liveness

- **macOS/Linux/WSL-inside:** `kill(pid, 0)` — `0` ⇒ alive, `EPERM` ⇒ alive (not
  ours), `ESRCH` ⇒ gone.
- **Windows native:** `Process.GetProcessById` / `OpenProcess(SYNCHRONIZE)`.
- **WSL from Windows:** the `pid` is a Linux PID — call
  `wsl.exe -d <distro> -- kill -0 <pid>`, or fall back to file-mtime staleness.

A session file may linger after its process dies; always confirm liveness before
showing a session.

## Transcript — `~/.claude/projects/<encoded-cwd>/<sessionId>.jsonl`

`<encoded-cwd>` is the `cwd` with **every non-alphanumeric character replaced by
`-`**. One JSON object per line, appended over time. Read from the end.

Fields we mine (best-effort, newest wins):

- `ai-title` entry → `aiTitle` (string): generated session title.
- `last-prompt` entry → `lastPrompt` (string): the user's most recent prompt.
- assistant message lines (`message.role == "assistant"`):
  - `message.content[].text` (type `"text"`) → most recent assistant text.
  - `message.usage` → context size =
    `input_tokens + cache_read_input_tokens + cache_creation_input_tokens`;
    `message.model` → the model of that turn.

### Context-window inference

Claude Code does not record the window size, so infer it from the model:
- Opus 4.x → **1,000,000** tokens.
- Otherwise → **200,000**, upgraded to 1,000,000 once observed usage exceeds 200K.
- Override with `CWATCH_CONTEXT_WINDOW` (`1m` or a raw integer).

## Security notes for path handling

- `sessionId` becomes a path component — reject any id that isn't
  `[A-Za-z0-9-]+` before joining (defense against `..` traversal).
- Same for the encoded `cwd` folder: derive it only by the replacement rule
  above; never trust it as an arbitrary path.
