param(
    [string]$RepoUrl = "https://gitcode.com/gcw_SpGZ48lW/mycodex-updates.git",

    [string]$WorkDirectory,

    [string]$CommitMessage = "Update package distribution repository",

    [string]$GitUsername,

    [string]$GitPassword
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$artifactRoot = Join-Path $repoRoot "artifacts"
. (Join-Path $repoRoot "tools\gitcode\GitCodePublishing.ps1")
. (Join-Path $repoRoot "tools\build\ScriptHelpers.ps1")

if ([string]::IsNullOrWhiteSpace($WorkDirectory)) {
    $WorkDirectory = Join-Path $artifactRoot "gitcode-distribution"
}

function Invoke-GitCodePush {
    param([string[]]$Arguments)

    $previousTerminalPrompt = $env:GIT_TERMINAL_PROMPT
    $previousGcmInteractive = $env:GCM_INTERACTIVE
    $previousAskPass = $env:GIT_ASKPASS
    $previousAskPassUsername = $env:GITCODE_ASKPASS_USERNAME
    $previousAskPassPassword = $env:GITCODE_ASKPASS_PASSWORD
    $askPassPath = $null

    try {
        $env:GIT_TERMINAL_PROMPT = "0"
        $env:GCM_INTERACTIVE = "Never"

        if (-not [string]::IsNullOrWhiteSpace($GitPassword)) {
            $askPassPath = Join-Path ([System.IO.Path]::GetTempPath()) ("mycodex-gitcode-askpass-{0}.cmd" -f [System.Guid]::NewGuid().ToString("N"))
            Set-Content -LiteralPath $askPassPath -Encoding ASCII -Value @"
@echo off
echo %~1 | findstr /I "Username" >nul
if not errorlevel 1 (
  echo %GITCODE_ASKPASS_USERNAME%
  exit /b 0
)
echo %GITCODE_ASKPASS_PASSWORD%
"@
            $env:GIT_ASKPASS = $askPassPath
            $env:GITCODE_ASKPASS_USERNAME = $(if ([string]::IsNullOrWhiteSpace($GitUsername)) { "gitcode" } else { $GitUsername })
            $env:GITCODE_ASKPASS_PASSWORD = $GitPassword
        }

        $gitArgs = @("-c", "credential.helper=") + $Arguments
        $exitCode = Invoke-GitCodeNativeCommand -FilePath "git" -Arguments $gitArgs
        if ($exitCode -ne 0) {
            throw "GitCode git push failed."
        }
    }
    finally {
        if ($null -eq $previousTerminalPrompt) { Remove-Item Env:\GIT_TERMINAL_PROMPT -ErrorAction SilentlyContinue } else { $env:GIT_TERMINAL_PROMPT = $previousTerminalPrompt }
        if ($null -eq $previousGcmInteractive) { Remove-Item Env:\GCM_INTERACTIVE -ErrorAction SilentlyContinue } else { $env:GCM_INTERACTIVE = $previousGcmInteractive }
        if ($null -eq $previousAskPass) { Remove-Item Env:\GIT_ASKPASS -ErrorAction SilentlyContinue } else { $env:GIT_ASKPASS = $previousAskPass }
        if ($null -eq $previousAskPassUsername) { Remove-Item Env:\GITCODE_ASKPASS_USERNAME -ErrorAction SilentlyContinue } else { $env:GITCODE_ASKPASS_USERNAME = $previousAskPassUsername }
        if ($null -eq $previousAskPassPassword) { Remove-Item Env:\GITCODE_ASKPASS_PASSWORD -ErrorAction SilentlyContinue } else { $env:GITCODE_ASKPASS_PASSWORD = $previousAskPassPassword }
        if ($askPassPath -and (Test-Path -LiteralPath $askPassPath)) {
            Remove-Item -LiteralPath $askPassPath -Force
        }
    }
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
Remove-DirectorySafely -Path $WorkDirectory -AllowedRoot $artifactRoot
New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null

Set-Content -LiteralPath (Join-Path $WorkDirectory "README.md") -Encoding UTF8 -Value @"
# MyCodex Updates

This public GitCode repository is used only for MyCodex release package distribution.
Source code is not published here.
"@

$sourceUpdates = Join-Path $repoRoot "updates"
if (Test-Path -LiteralPath $sourceUpdates) {
    Copy-Item -LiteralPath $sourceUpdates -Destination (Join-Path $WorkDirectory "updates") -Recurse -Force
}

$sourceGitCodeWorkflows = Join-Path $repoRoot ".gitcode\workflows"
if (Test-Path -LiteralPath $sourceGitCodeWorkflows) {
    $targetGitCodeDirectory = Join-Path $WorkDirectory ".gitcode"
    New-Item -ItemType Directory -Path $targetGitCodeDirectory -Force | Out-Null
    Copy-Item -LiteralPath $sourceGitCodeWorkflows -Destination (Join-Path $targetGitCodeDirectory "workflows") -Recurse -Force
}

Push-Location $WorkDirectory
try {
    git init -b main | Out-Null
    git config user.name "MyCodex Release Bot"
    git config user.email "release@mycodex.local"
    git add .
    git commit -m $CommitMessage | Out-Null
    git remote add gitcode-distribution $RepoUrl
    Invoke-GitCodePush -Arguments @("push", "--force", "gitcode-distribution", "main:main")
}
finally {
    Pop-Location
}

Write-Host "GitCode distribution repository updated:"
Write-Host "  $RepoUrl"
