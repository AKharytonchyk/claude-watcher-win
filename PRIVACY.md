# Privacy

Claude Watcher is **local-first**. It has **no telemetry, no analytics, and
makes no network requests of its own**. The only outbound path is the optional
`gh` PR lookup described below, and it is disable-able.

## What it reads (local, read-only)

Across every root — the native Windows home **and** each WSL distro:

- `%USERPROFILE%\.claude\sessions\*.json` and, for WSL,
  `\\wsl$\<distro>\home\<user>\.claude\sessions\*.json` — the status files that
  running Claude Code processes write (name, cwd, `busy`/`idle`/`waiting`,
  timestamps).
- `~/.claude/projects/<project>/<sessionId>.jsonl` — the session transcript, from
  which it extracts your **last prompt**, the **last assistant text**, and
  **token usage** (for the context gauge). This stays on your machine and is shown
  only in the local flyout.
- `<repo>\.git\HEAD` — to display the current branch.

It never modifies these files — on Windows or inside any WSL distro.

## What it writes

**Nothing.** No caches, no preferences, no logs, no persisted state, and nothing
written into a WSL distro. Everything is held in memory while the app runs.

## Processes it runs (and what data they receive)

| Command | When | Data passed | Network? |
|---------|------|-------------|----------|
| `wsl.exe -l -q` | on refresh | none | no (local) |
| `wsl.exe -d <distro> -- kill -0 <pid>` | on refresh, per WSL session | a process id | no (local) |
| `gh pr list --head <branch> …` (native or `wsl.exe -- gh`) | background, cached | **only the branch name** (gh reads the repo from your local git remote) | **yes** |

The **only** outbound traffic is that `gh` call, which talks to **your own**
GitHub host with **your own** `gh` credentials, and sends only the repo + branch.
Your prompts, transcripts, session contents, and file paths are **never** sent
anywhere.

Clicking a row focuses the hosting terminal window locally; clicking the PR pill
opens that PR URL in your browser — both are actions you initiate.

## Turning off all network

Set `CWATCH_OFFLINE` to disable the PR lookup entirely — then the app makes
**zero** network calls and spawns no `gh`:

```powershell
setx CWATCH_OFFLINE 1   # applies to processes started afterwards; relaunch Claude Watcher
```

(The app also stays fully offline automatically if `gh` isn't installed.)

## Permissions

- No special capabilities or entitlements; unpackaged, runs as your user.
- Reading `\\wsl$` uses your existing WSL access — no elevation, no extra grants.
- No accessibility, camera, mic, contacts, or location.
