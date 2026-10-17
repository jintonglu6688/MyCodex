$ErrorActionPreference = 'Stop'

$repositoryRoot = (& git rev-parse --show-toplevel 2>$null | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($repositoryRoot))
{
    throw 'Run this check inside the MyCodex repository.'
}

$policyPath = Join-Path $repositoryRoot '.codex\hooks\pre_tool_use_policy.ps1'
$sessionPath = Join-Path $repositoryRoot '.codex\hooks\session_context.ps1'
$configPath = Join-Path $repositoryRoot '.codex\hooks.json'

function Invoke-Policy
{
    param([Parameter(Mandatory = $true)][string] $Command)

    $payload = [ordered]@{
        hook_event_name = 'PreToolUse'
        tool_name = 'Bash'
        tool_input = [ordered]@{ command = $Command }
    } | ConvertTo-Json -Depth 5 -Compress

    $output = ($payload | powershell -NoProfile -ExecutionPolicy Bypass -File $policyPath) -join "`n"
    if ($LASTEXITCODE -ne 0)
    {
        throw 'PreToolUse hook failed.'
    }
    return $output
}

function Assert-Blocked
{
    param([Parameter(Mandatory = $true)][string] $Command)

    $result = Invoke-Policy -Command $Command
    if ($result -notmatch '"permissionDecision":"deny"')
    {
        throw "Expected command to be blocked: $Command"
    }
}

function Assert-Allowed
{
    param([Parameter(Mandatory = $true)][string] $Command)

    $result = Invoke-Policy -Command $Command
    if (-not [string]::IsNullOrWhiteSpace($result))
    {
        throw "Expected command to be allowed: $Command"
    }
}

$config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
$sessionMatcher = [string]$config.hooks.SessionStart[0].matcher
if ($null -ne $config.hooks.Stop)
{
    throw 'Hook configuration must omit the lexical Stop gate.'
}
foreach ($source in @('startup', 'resume', 'clear', 'compact'))
{
    if ($source -notmatch $sessionMatcher -or "other-$source" -match $sessionMatcher)
    {
        throw "SessionStart matcher must match only supported sources: $source"
    }
}
$toolMatcher = [string]$config.hooks.PreToolUse[0].matcher
if ('Bash' -notmatch $toolMatcher -or @('apply_patch', 'Edit', 'Write', 'mcp__test__Bash') -match $toolMatcher)
{
    throw 'The command policy must match Bash only.'
}
$sessionCommand = [string]$config.hooks.SessionStart[0].hooks[0].commandWindows
$encodedSessionCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($sessionCommand))

Assert-Allowed -Command 'git clean -ndx'
Assert-Allowed -Command 'git clean -nfdx'
Assert-Allowed -Command 'git clean --dry-run --force -d'
Assert-Allowed -Command "Write-Output 'git reset --hard'"
Assert-Allowed -Command "Remove-Item -LiteralPath 'E:\Temp\example' -Recurse"
Assert-Blocked -Command 'git clean -fd'
Assert-Blocked -Command 'git reset --hard'
Assert-Blocked -Command "Remove-Item 'E:\Temp\example' -Recurse"

function Invoke-SessionContext
{
    param([string] $Source, [string] $SessionId)

    $payload = @{
        hook_event_name = 'SessionStart'
        session_id = $SessionId
        source = $Source
    } | ConvertTo-Json -Compress
    $output = ($payload | powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedSessionCommand) -join "`n"
    if ($LASTEXITCODE -ne 0)
    {
        throw "SessionStart hook failed: $Source"
    }
    return ($output | ConvertFrom-Json).hookSpecificOutput.additionalContext
}

$probeId = 'hook-check-' + [Guid]::NewGuid().ToString('N')
$stateDirectory = Join-Path $repositoryRoot '.codex\session-state'
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$statePath = Join-Path $stateDirectory ($probeId + '.md')
$fixtureRoot = Join-Path $stateDirectory ($probeId + '-repo')
# Unicode escapes keep this test script readable by Windows PowerShell without a BOM.
$marker = ([string][char]0x6062) + [char]0x590D + [char]0x76EE + [char]0x6807 + ': ' + $probeId
try
{
    foreach ($hasState in @($false, $true))
    {
        if ($hasState)
        {
            [IO.File]::WriteAllText($statePath, $marker, [Text.UTF8Encoding]::new($false))
        }
        foreach ($source in @('startup', 'clear', 'resume', 'compact'))
        {
            $context = Invoke-SessionContext -Source $source -SessionId $probeId
            $shouldRestore = $hasState -and $source -in @('resume', 'compact')
            if ($context.Contains($marker) -ne $shouldRestore)
            {
                throw "Incorrect UTF-8 recovery: source=$source, hasState=$hasState"
            }
            if (-not $context.Contains("Recovery file: .codex/session-state/$probeId.md") -or
                -not $context.Contains('Working Sessions And Git Safety section in AGENTS.md.') -or
                -not $context.Contains('Current repository snapshot:'))
            {
                throw 'Missing session instructions or Git snapshot.'
            }
            $shouldReportMissing = -not $hasState -and $source -eq 'compact'
            if ($context.Contains('No saved working-state file was found after compaction.') -ne $shouldReportMissing)
            {
                throw 'Incorrect missing recovery note handling.'
            }
        }
    }

    [IO.File]::WriteAllText($statePath, $marker + ('x' * 11000) + 'END_OF_NOTE', [Text.UTF8Encoding]::new($false))
    Push-Location -LiteralPath (Join-Path $repositoryRoot 'src')
    try
    {
        $context = Invoke-SessionContext -Source 'compact' -SessionId $probeId
        if (-not $context.Contains($marker) -or $context.Contains('END_OF_NOTE') -or
            -not $context.Contains("Read the full recovery file at .codex/session-state/$probeId.md"))
        {
            throw 'Subdirectory recovery must preserve UTF-8 and point to the full truncated note.'
        }
    }
    finally
    {
        Pop-Location
    }

    # A disposable repository exercises truncated Git output without touching user files.
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot '.codex\hooks') -Force | Out-Null
    Copy-Item -LiteralPath $sessionPath -Destination (Join-Path $fixtureRoot '.codex\hooks\session_context.ps1')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot '.gitignore') -Destination $fixtureRoot
    & git init --quiet $fixtureRoot
    if ($LASTEXITCODE -ne 0) { throw 'Failed to initialize hook test repository.' }
    Push-Location -LiteralPath $fixtureRoot
    try
    {
        foreach ($source in @('startup', 'resume', 'compact'))
        {
            $context = Invoke-SessionContext -Source $source -SessionId $probeId
            if ($context.Contains($marker) -or $context.Contains('Saved working state:') -or
                -not $context.Contains("Repository root: $($fixtureRoot.Replace('\', '/'))") -or
                (Test-Path -LiteralPath (Join-Path $fixtureRoot '.codex\session-state')))
            {
                throw 'A fresh checkout must report its own root and require no old state or generated directory.'
            }
        }
        & git check-ignore --quiet '.codex/session-state/example.md'
        if ($LASTEXITCODE -ne 0) { throw 'Recovery notes must be ignored in a fresh checkout.' }
    }
    finally
    {
        Pop-Location
    }
    foreach ($index in 1..61)
    {
        [IO.File]::WriteAllText((Join-Path $fixtureRoot "file-$index.txt"), '')
    }
    Push-Location -LiteralPath $fixtureRoot
    try
    {
        $context = Invoke-SessionContext -Source 'startup' -SessionId $probeId
        if (-not $context.Contains('[Git status truncated; run git status --short for the full list.]'))
        {
            throw 'Partial Git status must be marked as truncated.'
        }
    }
    finally
    {
        Pop-Location
    }
}
finally
{
    if (Test-Path -LiteralPath $statePath)
    {
        Remove-Item -LiteralPath $statePath -ErrorAction Stop
    }
    $resolvedFixtureRoot = [IO.Path]::GetFullPath($fixtureRoot)
    $allowedPrefix = [IO.Path]::GetFullPath($stateDirectory).TrimEnd('\') + '\'
    if (-not $resolvedFixtureRoot.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase))
    {
        throw 'Refusing to clean up a fixture outside the session-state directory.'
    }
    if (Test-Path -LiteralPath $resolvedFixtureRoot)
    {
        Remove-Item -LiteralPath $resolvedFixtureRoot -Recurse -Force -ErrorAction Stop
    }
}

'Hook checks passed.'
