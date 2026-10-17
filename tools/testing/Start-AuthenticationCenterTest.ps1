[CmdletBinding()]
param(
    [string]$HostExecutable,
    [string]$CodexHome,
    [string]$ApplicationExecutable
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$applicationPath = if ([string]::IsNullOrWhiteSpace($ApplicationExecutable)) { Join-Path $repositoryRoot 'debug\MyCodex.Dev.exe' } else { $ApplicationExecutable }
if ([string]::IsNullOrWhiteSpace($HostExecutable)) {
    $HostExecutable = Join-Path ([IO.Path]::GetDirectoryName($applicationPath)) 'runtime\auth-core\mycodex-auth-host.exe'
}
foreach ($file in @($applicationPath, $HostExecutable)) {
    if (-not [IO.Path]::IsPathRooted($file) -or -not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required executable not found: $file. Run build-dev.bat from the repository root to prepare the pinned authentication backend and build the MyCodex development app."
    }
}
$hostManifest = Join-Path ([IO.Path]::GetDirectoryName($HostExecutable)) 'mycodex-auth-host.manifest.json'
if (-not (Test-Path -LiteralPath $hostManifest -PathType Leaf)) {
    throw 'The authentication host manifest is missing. Prepare the complete backend package before starting the development app.'
}
$manifest = Get-Content -LiteralPath $hostManifest -Encoding UTF8 -Raw | ConvertFrom-Json
if ($manifest.protocolVersion -ne 3) { throw 'Unsupported authentication backend protocol.' }
if ([string]::IsNullOrWhiteSpace($CodexHome)) {
    $CodexHome = Join-Path $env:LOCALAPPDATA 'MyCodex-auth-core-test\codex-home'
}
if (Get-Process -Name 'MyCodex.Dev' -ErrorAction SilentlyContinue) {
    throw 'Close the running MyCodex development application before using this launcher. Your installed application can stay open.'
}
if (-not [IO.Path]::IsPathRooted($CodexHome)) {
    throw 'CodexHome must be an absolute path.'
}
$targetHome = [IO.Path]::GetFullPath($CodexHome)
[IO.Directory]::CreateDirectory($targetHome) | Out-Null
$startInfo = New-Object Diagnostics.ProcessStartInfo
$startInfo.FileName = $applicationPath
$startInfo.WorkingDirectory = $repositoryRoot
$startInfo.UseShellExecute = $false
$startInfo.EnvironmentVariables.Remove('MYCODEX_AUTH_HOST_PATH')
$startInfo.EnvironmentVariables.Remove('MYCODEX_AUTH_CORE_PATH')
$startInfo.EnvironmentVariables['MYCODEX_AUTH_CORE_PATH'] = [IO.Path]::GetFullPath($HostExecutable)
$startInfo.EnvironmentVariables['CODEX_HOME'] = $targetHome
[Diagnostics.Process]::Start($startInfo) | Out-Null
Write-Host "Development application started. Authentication Center target: $targetHome"
Write-Host 'Open the top user menu, then Authentication Center. Its backend starts automatically.'
