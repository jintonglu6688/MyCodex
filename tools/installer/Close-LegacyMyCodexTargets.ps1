param([Parameter(Mandatory = $true)][string]$InstallRoot)
$ErrorActionPreference = 'Stop'
try {
    Add-Type -Path (Join-Path $PSScriptRoot 'LegacyTargetExit.cs')
    exit [MyCodexInstaller.LegacyTargetExit]::Run($InstallRoot, 120000)
} catch {
    [Console]::Error.WriteLine('legacy_target_exit_inspection_failed')
    exit 2
}
