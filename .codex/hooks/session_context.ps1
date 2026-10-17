$ErrorActionPreference = 'Stop'

function Read-HookInput
{
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw))
    {
        return $null
    }

    return $raw | ConvertFrom-Json
}

function Write-AdditionalContext
{
    param([Parameter(Mandatory = $true)][string] $Context)

    $output = [ordered]@{
        hookSpecificOutput = [ordered]@{
            hookEventName = 'SessionStart'
            additionalContext = $Context
        }
    }

    $output | ConvertTo-Json -Depth 5 -Compress
}

function Get-SafeSessionId
{
    param([string] $SessionId)

    if ([string]::IsNullOrWhiteSpace($SessionId))
    {
        return 'unknown-session'
    }

    return $SessionId -replace '[^A-Za-z0-9._-]', '_'
}

function Get-GitSnapshot
{
    $branch = (& git branch --show-current 2>$null | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($branch))
    {
        $branch = '(detached or unavailable)'
    }

    $statusLines = @(& git status --short 2>$null | Select-Object -First 61)
    $status = if ($statusLines.Count -eq 0)
    {
        '(clean)'
    }
    else
    {
        ($statusLines | Select-Object -First 60) -join "`n"
    }
    if ($statusLines.Count -gt 60)
    {
        $status += "`n[Git status truncated; run git status --short for the full list.]"
    }

    return @"
Branch: $branch
Working tree:
$status
"@
}

$payload = Read-HookInput
if ($null -eq $payload -or [string]$payload.hook_event_name -ne 'SessionStart')
{
    exit 0
}

$source = [string]$payload.source
$sessionId = Get-SafeSessionId -SessionId ([string]$payload.session_id)
$repositoryRoot = (& git rev-parse --show-toplevel 2>$null | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($repositoryRoot))
{
    exit 0
}

$stateRelativePath = ".codex/session-state/$sessionId.md"
$statePath = Join-Path $repositoryRoot ($stateRelativePath -replace '/', '\')
$gitSnapshot = Get-GitSnapshot

$context = @"
MyCodex session continuity:
- Repository root: $repositoryRoot
- Recovery file: $stateRelativePath (ignored by Git). For long work, follow the Working Sessions And Git Safety section in AGENTS.md.
- Record outcomes, decisions, next steps, verification, and risks after material milestones; never store secrets, private data, or transcripts.
- The latest user request, AGENTS.md, and actual repository state override saved notes.

Current repository snapshot:
$gitSnapshot
"@

if (($source -eq 'resume' -or $source -eq 'compact') -and (Test-Path -LiteralPath $statePath -PathType Leaf))
{
    $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8
    if ($state.Length -gt 10000)
    {
        $state = $state.Substring(0, 10000) + "`n[Recovery note truncated. Read the full recovery file at $stateRelativePath before continuing.]"
    }

    $context += @"

Saved working state:
---
$state
---
Reconcile these notes with the latest user request and git snapshot before continuing.
"@
}
elseif ($source -eq 'compact')
{
    $context += @"

No saved working-state file was found after compaction. Before editing, reconstruct the active objective and remaining work from the latest user request, recent files, and git diff. Create the recovery file if the task remains long-running.
"@
}

Write-AdditionalContext -Context $context
