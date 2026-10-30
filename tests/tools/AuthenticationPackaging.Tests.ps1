[CmdletBinding()]
param([string]$PackageRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$authTools = Join-Path $repoRoot 'tools\authentication-center'
if ([string]::IsNullOrWhiteSpace($PackageRoot)) { $PackageRoot = Join-Path $repoRoot 'runtime\auth-core' }
. (Join-Path $authTools 'Test-AuthenticationHostPackage.ps1')
$original = Test-AuthenticationHostPackage -PackageRoot $PackageRoot -AllowDevelopment
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('mycodex-auth-package-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
function Expect-Failure([scriptblock]$Action) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    if (-not $failed) { throw 'Expected packaging validation to reject this fixture.' }
}
try {
    foreach ($name in @('mycodex-auth-host.exe','mycodex-auth-host.manifest.json','LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $PackageRoot $name) -Destination (Join-Path $testRoot $name)
    }
    Test-AuthenticationHostPackage -PackageRoot $testRoot -AllowDevelopment | Out-Null
    & (Join-Path $authTools 'Prepare-AuthenticationHost.ps1') -Configuration Debug -DestinationRoot $testRoot
    if ((Get-FileHash -LiteralPath (Join-Path $testRoot 'mycodex-auth-host.exe') -Algorithm SHA256).Hash -ine $original.sha256) {
        throw 'Debug preparation replaced the verified local authentication backend.'
    }
    if ($original.sourceDirty) { Expect-Failure { Test-AuthenticationHostPackage -PackageRoot $testRoot } }
    $manifestPath = Join-Path $testRoot 'mycodex-auth-host.manifest.json'
    $originalText = [IO.File]::ReadAllText($manifestPath)
    foreach ($change in @(@{ protocolVersion = 999 }, @{ sourceRevision = 'unknown' }, @{ sha256 = ('0' * 64) }, @{ target = 'unknown' })) {
        $manifest = $originalText | ConvertFrom-Json
        foreach ($key in $change.Keys) { $manifest.$key = $change[$key] }
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json))
        Expect-Failure { Test-AuthenticationHostPackage -PackageRoot $testRoot -AllowDevelopment }
        Expect-Failure { & (Join-Path $authTools 'Prepare-AuthenticationHost.ps1') -Configuration Debug -DestinationRoot $testRoot }
    }
    [IO.File]::WriteAllText($manifestPath, $originalText)
    [IO.File]::WriteAllText((Join-Path $testRoot 'LICENSE'), '')
    Expect-Failure { Test-AuthenticationHostPackage -PackageRoot $testRoot -AllowDevelopment }
    $installer = Join-Path $testRoot 'fixture.exe'
    [IO.File]::WriteAllText($installer, 'fixture-not-executed')
    Expect-Failure { Test-AuthenticationInstallerReceipt -InstallerPath $installer }
    $clean = $originalText | ConvertFrom-Json
    $clean.sourceDirty = $false
    $receipt = @{ schemaVersion = 1; development = $false; installerSha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash; authenticationHost = $clean }
    [IO.File]::WriteAllText(($installer + '.auth.json'), ($receipt | ConvertTo-Json -Depth 5))
    Test-AuthenticationInstallerReceipt -InstallerPath $installer
    $manifestScript = Join-Path $repoRoot 'tools\update\New-MyCodexUpdateManifest.ps1'
    $updatePath = Join-Path $testRoot 'update.json'
    & $manifestScript -Version '0.0.1' -InstallerPath $installer -PackageBaseUrl 'https://example.invalid/releases/' -OutputPath $updatePath
    if (-not (Test-Path -LiteralPath $updatePath)) { throw 'Valid receipt did not permit update manifest generation.' }
    [IO.File]::WriteAllText($installer, 'changed-fixture')
    Expect-Failure { Test-AuthenticationInstallerReceipt -InstallerPath $installer }
    $before = [IO.File]::ReadAllText($updatePath)
    Expect-Failure { & $manifestScript -Version '0.0.1' -InstallerPath $installer -PackageBaseUrl 'https://example.invalid/releases/' -OutputPath $updatePath }
    if ([IO.File]::ReadAllText($updatePath) -cne $before) { throw 'Rejected installer changed the update manifest.' }
    Write-Host 'Authentication package and installer receipt checks passed.'
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $allowed = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -notlike 'mycodex-auth-package-*') { throw 'Refusing fixture cleanup outside temporary root.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
