$ErrorActionPreference = 'Stop'

$repositoryRoot = (& git rev-parse --show-toplevel 2>$null | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($repositoryRoot))
{
    throw 'Run this check inside the MyCodex repository.'
}

$policyPath = Join-Path $repositoryRoot '.codex\hooks\pre_tool_use_policy.ps1'
$sessionPath = Join-Path $repositoryRoot '.codex\hooks\session_context.ps1'
$configPath = Join-Path $repositoryRoot '.codex\hooks.json'
$claudeConfigPath = Join-Path $repositoryRoot '.claude\settings.json'

function Invoke-Policy
{
    param([Parameter(Mandatory = $true)][string] $Command, [string] $ToolName = 'Bash')

    $payload = [ordered]@{
        hook_event_name = 'PreToolUse'
        tool_name = $ToolName
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
    param([Parameter(Mandatory = $true)][string] $Command, [string] $ToolName = 'Bash')

    $result = Invoke-Policy -Command $Command -ToolName $ToolName
    if ($result -notmatch '"permissionDecision":"deny"')
    {
        throw "Expected command to be blocked: $ToolName $Command"
    }
}

function Assert-Allowed
{
    param([Parameter(Mandatory = $true)][string] $Command, [string] $ToolName = 'Bash')

    $result = Invoke-Policy -Command $Command -ToolName $ToolName
    if (-not [string]::IsNullOrWhiteSpace($result))
    {
        throw "Expected command to be allowed: $ToolName $Command"
    }
}

function Assert-HookConfiguration
{
    param(
        [Parameter(Mandatory = $true)] $Config,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string[]] $CommandTools)

    if ($null -ne $Config.hooks.Stop)
    {
        throw "$Name hook configuration must omit the lexical Stop gate."
    }
    $sessionMatcher = [string]$Config.hooks.SessionStart[0].matcher
    foreach ($source in @('startup', 'resume', 'clear', 'compact'))
    {
        if ($source -notmatch $sessionMatcher -or "other-$source" -match $sessionMatcher)
        {
            throw "$Name SessionStart matcher must match only supported sources: $source"
        }
    }
    $toolMatcher = [string]$Config.hooks.PreToolUse[0].matcher
    $otherTools = @('apply_patch', 'Edit', 'Write', 'mcp__test__Bash', 'mcp__test__PowerShell', 'PowerShellPreview') |
        Where-Object { $CommandTools -notcontains $_ }
    if (@($CommandTools | Where-Object { $_ -notmatch $toolMatcher }).Count -gt 0 -or @($otherTools -match $toolMatcher).Count -gt 0)
    {
        throw "$Name command policy must match only: $($CommandTools -join ', ')"
    }
    if ([string]$Config.hooks.SessionStart[0].hooks[0].command -notmatch [regex]::Escape('.codex\hooks\session_context.ps1') -or
        [string]$Config.hooks.PreToolUse[0].hooks[0].command -notmatch [regex]::Escape('.codex\hooks\pre_tool_use_policy.ps1'))
    {
        throw "$Name hooks must run the shared .codex hook scripts."
    }
}

function ConvertTo-EncodedCommand
{
    param([Parameter(Mandatory = $true)][string] $Command)

    return [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command))
}

$config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
$claudeConfig = Get-Content -LiteralPath $claudeConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-HookConfiguration -Config $config -Name 'Codex' -CommandTools @('Bash')
Assert-HookConfiguration -Config $claudeConfig -Name 'Claude Code' -CommandTools @('Bash', 'PowerShell')
$encodedSessionCommand = ConvertTo-EncodedCommand -Command ([string]$config.hooks.SessionStart[0].hooks[0].commandWindows)
$encodedClaudeSessionCommand = ConvertTo-EncodedCommand -Command ([string]$claudeConfig.hooks.SessionStart[0].hooks[0].command)

foreach ($toolName in @('Bash', 'PowerShell'))
{
    Assert-Allowed -ToolName $toolName -Command 'git clean -ndx'
    Assert-Allowed -ToolName $toolName -Command 'git clean -nfdx'
    Assert-Allowed -ToolName $toolName -Command 'git clean --dry-run --force -d'
    Assert-Allowed -ToolName $toolName -Command "Write-Output 'git reset --hard'"
    Assert-Allowed -ToolName $toolName -Command "Remove-Item -LiteralPath 'E:\Temp\example' -Recurse"
    Assert-Blocked -ToolName $toolName -Command 'git clean -fd'
    Assert-Blocked -ToolName $toolName -Command 'git reset --hard'
    Assert-Blocked -ToolName $toolName -Command "Remove-Item 'E:\Temp\example' -Recurse"
}
Assert-Blocked -ToolName 'PowerShell' -Command "git status; git reset --hard"
Assert-Allowed -ToolName 'Write' -Command 'git reset --hard'

function Invoke-SessionContext
{
    param([string] $Source, [string] $SessionId, [string] $EncodedCommand = $encodedSessionCommand)

    $payload = @{
        hook_event_name = 'SessionStart'
        session_id = $SessionId
        source = $Source
    } | ConvertTo-Json -Compress
    $output = ($payload | powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand $EncodedCommand) -join "`n"
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
            $claudeContext = Invoke-SessionContext -Source $source -SessionId $probeId -EncodedCommand $encodedClaudeSessionCommand
            if ($claudeContext -ne $context)
            {
                throw "Claude Code SessionStart hook must match the Codex context: source=$source, hasState=$hasState"
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
