<#
.SYNOPSIS
    Build a demo fleet for documentation screenshots, then run the app against it.

.DESCRIPTION
    Writes a fixture that mimics ~/.claude for several origins — session files,
    transcripts, and throwaway git repos so branches resolve — and launches the app
    with CWATCH_DEMO pointed at it. Your real ~/.claude is never read or written.

    Every state is represented on purpose: one agent waiting on you (red), one
    working (yellow), idle ones (green), plus context pressure high enough to show
    the amber and red ctx% treatments.

.EXAMPLE
    ./tools/demo-data.ps1
    ./tools/demo-data.ps1 -NoLaunch      # just build the fixture
#>
[CmdletBinding()]
param(
    [string] $Root = (Join-Path $env:TEMP 'cwatch-demo'),
    [switch] $NoLaunch,
    # Adversarial fleet instead of the pretty one: absurd names, branches and prompts,
    # plus enough agents to overflow, to prove nothing overlaps, clips or escapes the
    # screen. Not for screenshots.
    [switch] $Stress
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }

# Claude Code encodes a transcript folder as the cwd with every non-alphanumeric
# character replaced by '-'. TranscriptReader applies the same rule.
function Get-EncodedCwd([string] $cwd) {
    -join ($cwd.ToCharArray() | ForEach-Object { if ($_ -match '[A-Za-z0-9]') { $_ } else { '-' } })
}

function New-DemoRepo([string] $path, [string] $branch) {
    New-Item -ItemType Directory -Force -Path (Join-Path $path '.git') | Out-Null
    Set-Content -LiteralPath (Join-Path $path '.git\HEAD') -Value "ref: refs/heads/$branch" -Encoding ascii
    return $path
}

function New-DemoAgent {
    param(
        [string] $Origin,        # folder name → the row's origin label
        [string] $Name,
        [string] $Cwd,
        [string] $Status,        # waiting | busy | idle
        [string] $WaitingFor,
        [int]    $AgentPid,
        [int]    $AgeMinutes,
        [string] $Prompt,
        [string] $Said,
        [int]    $Tokens,
        [string] $Model = 'claude-opus-5'
    )

    $originHome = Join-Path $Root $Origin
    $sessions = Join-Path $originHome '.claude\sessions'
    New-Item -ItemType Directory -Force -Path $sessions | Out-Null

    $sid = [guid]::NewGuid().ToString()
    $now = [DateTimeOffset]::UtcNow
    $started = $now.AddMinutes(-$AgeMinutes - 20).ToUnixTimeMilliseconds()
    $changed = $now.AddMinutes(-$AgeMinutes).ToUnixTimeMilliseconds()

    $session = [ordered]@{
        pid             = $AgentPid
        sessionId       = $sid
        cwd             = $Cwd
        name            = $Name
        version         = '2.1.220'
        kind            = 'interactive'
        status          = $Status
        startedAt       = $started
        updatedAt       = $changed
        statusUpdatedAt = $changed
    }
    if ($WaitingFor) { $session.waitingFor = $WaitingFor }

    # BOM-less UTF-8 throughout: Set-Content -Encoding utf8 emits a BOM on PS 5.1, and
    # a BOM at the head of a transcript makes its first line unparseable.
    $noBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText(
        (Join-Path $sessions "$AgentPid.json"), ($session | ConvertTo-Json -Compress), $noBom)

    # Transcript: drives the intent line, the model label and the ctx% gauge.
    if ($Prompt) {
        $dir = Join-Path $originHome ".claude\projects\$(Get-EncodedCwd $Cwd)"
        New-Item -ItemType Directory -Force -Path $dir | Out-Null

        # Compact JSON on purpose: the parser looks for the literal "role":"assistant".
        $lines = @(
            (@{ type = 'last-prompt'; lastPrompt = $Prompt } | ConvertTo-Json -Compress)
            ('{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":' +
             ($Said | ConvertTo-Json) +
             '}],"usage":{"input_tokens":' + $Tokens + '},"model":"' + $Model + '"}}')
        )
        [System.IO.File]::WriteAllText((Join-Path $dir "$sid.jsonl"), ($lines -join "`n") + "`n", $noBom)
    }
}

$repos = Join-Path $Root '_repos'

if ($Stress) {
    # Every field pushed past what the row can show, and 14 agents so the list must
    # scroll rather than grow the window off the top of the screen.
    $long   = 'a-really-quite-absurdly-long-agent-name-that-should-be-trimmed-not-overflow'
    $branch = 'feature/very/deeply/nested/branch-name-that-keeps-going-and-going-and-going-past-any-sane-width'
    $prompt = 'This prompt is deliberately far longer than the row can display, to confirm it ellipsises on a single line rather than wrapping, pushing the meta row down, or spilling out over the neighbouring agent underneath it.'

    New-DemoAgent -Origin 'Terminal' -Name $long `
        -Cwd (New-DemoRepo (Join-Path $repos 'long-branch') $branch) `
        -Status 'waiting' -WaitingFor 'permission prompt' -AgentPid 9001 -AgeMinutes 1 `
        -Prompt $prompt -Said $prompt -Tokens 199000

    New-DemoAgent -Origin 'A Ridiculously Long Origin Label Indeed' -Name 'origin-overflow-a1' `
        -Cwd (New-DemoRepo (Join-Path $repos 'origin-overflow') 'main') `
        -Status 'busy' -AgentPid 9002 -AgeMinutes 2 `
        -Prompt 'short' -Said 'short' -Tokens 150000

    # A deep path with no repo, so the path fallback is the long thing.
    $deep = Join-Path $repos 'a/very/deep/path/without/any/git/repository/at/all/whatsoever/inside/it'
    New-Item -ItemType Directory -Force -Path $deep | Out-Null
    New-DemoAgent -Origin 'VS Code' -Name 'deep-path-b2' -Cwd $deep `
        -Status 'idle' -AgentPid 9003 -AgeMinutes 3 -Prompt 'x' -Said 'y' -Tokens 12000

    # Bulk, to force scrolling.
    1..11 | ForEach-Object {
        $state = @('idle','busy','waiting')[($_ % 3)]
        New-DemoAgent -Origin @('Terminal','VS Code','Ubuntu')[($_ % 3)] `
            -Name "filler-agent-number-$_" `
            -Cwd (New-DemoRepo (Join-Path $repos "filler$_") "topic/filler-$_") `
            -Status $state -WaitingFor $(if ($state -eq 'waiting') { 'input needed' } else { $null }) `
            -AgentPid (9100 + $_) -AgeMinutes (5 * $_) `
            -Prompt "filler prompt $_" -Said "filler reply $_" -Tokens (30000 + $_ * 9000)
    }

    Write-Host "STRESS fixture: $Root"
    if (-not $NoLaunch) {
        $exe = Join-Path $repoRoot 'src\ClaudeWatcher\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\ClaudeWatcher.exe'
        Get-Process ClaudeWatcher -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 500
        $env:CWATCH_DEMO = $Root
        Start-Process -FilePath $exe | Out-Null
        Write-Host "launched with CWATCH_DEMO=$Root"
    }
    return
}

# --- the demo fleet -------------------------------------------------------------
# Red: blocked on you, and nearly out of context.
New-DemoAgent -Origin 'Terminal' -Name 'comic-project-b5' `
    -Cwd (New-DemoRepo (Join-Path $repos 'comic-project') 'feat/epub-ru') `
    -Status 'waiting' -WaitingFor 'permission prompt' -AgentPid 4821 -AgeMinutes 2 `
    -Prompt 'I mean the one with epub, translated to russian' `
    -Said 'Ready to run the conversion — need your approval for the write.' -Tokens 194000

# Yellow: working, in this actual repo, so the PR pill resolves for real.
New-DemoAgent -Origin 'VS Code' -Name 'claude-watcher-win-a7' `
    -Cwd $repoRoot -Status 'busy' -AgentPid 5137 -AgeMinutes 4 `
    -Prompt 'port the transcript tail read from the mac repo' `
    -Said 'Tail read is in; 181 ms down to 7 ms per refresh on a 70 MB transcript.' -Tokens 124000

# Green with amber pressure.
New-DemoAgent -Origin 'Ubuntu' -Name 'api-gateway-c2' `
    -Cwd (New-DemoRepo (Join-Path $repos 'api-gateway') 'fix/rate-limit-headers') `
    -Status 'idle' -AgentPid 6604 -AgeMinutes 13 `
    -Prompt 'add the retry-after header to the 429 path' `
    -Said 'Added, with a test for the header on repeated 429s.' -Tokens 158000

New-DemoAgent -Origin 'Terminal' -Name 'protectors-agents-b9' `
    -Cwd (New-DemoRepo (Join-Path $repos 'protectors-agents') 'feat/016-reasoning-compat') `
    -Status 'idle' -AgentPid 7290 -AgeMinutes 31 `
    -Prompt 'does the reasoning budget survive a tool call?' `
    -Said 'It does — the budget is per-turn, so tool results do not reset it.' -Tokens 88000

New-DemoAgent -Origin 'VS Code' -Name 'dotfiles-e3' `
    -Cwd (New-DemoRepo (Join-Path $repos 'dotfiles') 'main') `
    -Status 'idle' -AgentPid 8115 -AgeMinutes 48 `
    -Prompt 'split the powershell profile into modules' `
    -Said 'Split into three modules and dot-sourced them from the profile.' -Tokens 41000

Write-Host "demo fixture: $Root"
Get-ChildItem $Root -Directory | Where-Object Name -ne '_repos' | ForEach-Object {
    $n = (Get-ChildItem (Join-Path $_.FullName '.claude\sessions') -Filter *.json).Count
    Write-Host "  $($_.Name): $n session(s)"
}

if ($NoLaunch) { return }

$exe = Join-Path $repoRoot 'src\ClaudeWatcher\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\ClaudeWatcher.exe'
if (-not (Test-Path $exe)) { throw "build first: dotnet build ClaudeWatcher.sln -c Debug  (missing $exe)" }

Get-Process ClaudeWatcher -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$env:CWATCH_DEMO = $Root
Start-Process -FilePath $exe | Out-Null
Write-Host "launched with CWATCH_DEMO=$Root — open the tray flyout."
