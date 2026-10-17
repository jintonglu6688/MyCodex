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

function Write-Deny
{
    param([Parameter(Mandatory = $true)][string] $Reason)

    $output = [ordered]@{
        hookSpecificOutput = [ordered]@{
            hookEventName = 'PreToolUse'
            permissionDecision = 'deny'
            permissionDecisionReason = $Reason
        }
    }

    $output | ConvertTo-Json -Depth 5 -Compress
}

$payload = Read-HookInput
if ($null -eq $payload)
{
    exit 0
}

$toolName = [string]$payload.tool_name
$command = [string]$payload.tool_input.command

if ($toolName -eq 'Bash' -and -not [string]::IsNullOrWhiteSpace($command))
{
    # ponytail: direct-command patterns only; use a shell parser if broader coverage becomes necessary.
    $destructiveGitPatterns = @(
        '(?im)(?:^|[;&|]\s*)git\s+reset\s+--hard(?:\s|$)',
        '(?im)(?:^|[;&|]\s*)git\s+clean\s+(?![^\r\n;&|]*(?:--dry-run(?:\s|$)|-[A-Za-z]*n[A-Za-z]*(?:\s|$)))(?=[^\r\n;&|]*(?:--force(?:\s|$)|-[A-Za-z]*f[A-Za-z]*(?:\s|$)))',
        '(?im)(?:^|[;&|]\s*)git\s+checkout\s+--\s+\.(?:\s|$)',
        '(?im)(?:^|[;&|]\s*)git\s+restore\s+\.(?:\s|$)'
    )

    if ($destructiveGitPatterns | Where-Object { $command -match $_ } | Select-Object -First 1)
    {
        Write-Deny -Reason 'Blocked by this repository development hook: use a scoped Git operation that preserves unrelated changes. If the task explicitly requires a full reset, report the hook conflict.'
        exit 0
    }

    $recursiveDelete = [regex]::Match(
        $command,
        '(?im)(?:^|[;&|]\s*)Remove-Item\b[^\r\n;&|]*-Recurse\b[^\r\n;&|]*')
    if ($recursiveDelete.Success -and
        $recursiveDelete.Value -notmatch '(?i)GetFullPath|Resolve-Path|-LiteralPath')
    {
        Write-Deny -Reason 'Blocked by this repository development hook: verify the target is within the intended directory and use LiteralPath for recursive Remove-Item. This hook checks command text, not the resolved target.'
        exit 0
    }
}
