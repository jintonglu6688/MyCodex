[CmdletBinding()]
param(
    [string]$SourceRoot,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$DestinationRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) { $DestinationRoot = Join-Path $repositoryRoot 'runtime\auth-core' }
if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    if ($Configuration -eq 'Debug' -and (Test-Path -LiteralPath (Join-Path $DestinationRoot 'mycodex-auth-host.manifest.json') -PathType Leaf)) {
        . (Join-Path $PSScriptRoot 'Test-AuthenticationHostPackage.ps1')
        $existing = Test-AuthenticationHostPackage -PackageRoot $DestinationRoot -AllowDevelopment
        Write-Host "Using verified local authentication backend for Debug: $($existing.sourceRevision); uncommitted changes: $($existing.sourceDirty)"
        return
    }
    & (Join-Path $PSScriptRoot 'Prepare-AuthenticationHostRelease.ps1') -DestinationRoot $DestinationRoot
    return
}
$sourcePath = [IO.Path]::GetFullPath($SourceRoot)
$destinationPath = [IO.Path]::GetFullPath($DestinationRoot)
$cargoManifest = Join-Path $sourcePath 'src-tauri\Cargo.toml'
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'src-tauri\src\mycodex_host.rs') -PathType Leaf)) {
    throw 'Authentication host source is missing. Check out the published authentication feature branch first.'
}
$revision = & git -C $sourcePath rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$') { throw 'Cannot identify the authentication host source commit.' }
$changes = & git -C $sourcePath status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine whether the authentication host source is clean.' }
$dirty = -not [string]::IsNullOrWhiteSpace(($changes -join "`n"))
$cargoArguments = @('build', '--locked', '--manifest-path', $cargoManifest, '--bin', 'mycodex-auth-host', '--message-format=json-render-diagnostics')
if ($Configuration -eq 'Release') { $cargoArguments += '--release' }
$cargoOutput = & cargo @cargoArguments
if ($LASTEXITCODE -ne 0) { throw 'Authentication host build failed. No package was replaced.' }
$executable = $null
foreach ($line in $cargoOutput) {
    $artifact = $line | ConvertFrom-Json
    if ($artifact.reason -eq 'compiler-artifact' -and $artifact.target.name -eq 'mycodex-auth-host' -and $artifact.executable) {
        $executable = $artifact.executable
    }
}
if ([string]::IsNullOrWhiteSpace($executable) -or -not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'The Windows authentication host executable was not produced.' }
$identityText = & $executable --version-json
if ($LASTEXITCODE -ne 0) { throw 'The backend does not support artifact identification. Check the source version.' }
$identity = $identityText | ConvertFrom-Json
$currentRevision = & git -C $sourcePath rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $currentRevision -ne $revision) { throw 'The source commit changed during the build. Run preparation again.' }
$currentChanges = & git -C $sourcePath status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0 -or ((-not [string]::IsNullOrWhiteSpace(($currentChanges -join "`n"))) -ne $dirty)) {
    throw 'The source cleanliness changed during the build. Run preparation again.'
}
if ($identity.hostVersion -ne '0.3.7' -or $identity.protocolVersion -ne 3 -or
    $identity.upstreamRevision -ne '790ed8009df809bedaf6c31b5ced05da46bb1f1f' -or
    $identity.sourceRevision -ne $revision -or $identity.sourceDirty -isnot [bool] -or $identity.sourceDirty -ne $dirty -or
    $identity.target -ne 'x86_64-pc-windows-msvc') {
    throw 'The built backend identity does not match the selected source or supported Windows target.'
}
$license = Join-Path $sourcePath 'LICENSE'
if (-not (Test-Path -LiteralPath $license -PathType Leaf)) { throw 'The upstream license is missing.' }
$package = [ordered]@{
    schemaVersion = 1
    hostVersion = $identity.hostVersion
    protocolVersion = $identity.protocolVersion
    upstreamRevision = $identity.upstreamRevision
    sourceRevision = $identity.sourceRevision
    sourceDirty = $identity.sourceDirty
    target = $identity.target
    executable = 'mycodex-auth-host.exe'
    sha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
}
$packageExecutable = Join-Path $destinationPath $package.executable
if ($identity.sha256 -cne $package.sha256) { throw 'The backend self-reported hash does not match its executable.' }
if ([string]::Equals([IO.Path]::GetFullPath($executable), $packageExecutable, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package output must be separate from the Cargo build directory.'
}
[IO.Directory]::CreateDirectory($destinationPath) | Out-Null
Copy-Item -LiteralPath $executable -Destination $packageExecutable -Force
Copy-Item -LiteralPath $license -Destination (Join-Path $destinationPath 'LICENSE') -Force
if ((Get-FileHash -LiteralPath $packageExecutable -Algorithm SHA256).Hash -ine $package.sha256) {
    throw 'The copied backend failed integrity verification.'
}
[IO.File]::WriteAllText((Join-Path $destinationPath 'mycodex-auth-host.manifest.json'),
    (($package | ConvertTo-Json) + "`n"), [Text.UTF8Encoding]::new($false))
Write-Host "Authentication backend prepared: $packageExecutable"
Write-Host "Source: $revision; uncommitted changes: $dirty; protocol: $($identity.protocolVersion)"
if ($dirty) { Write-Warning 'This is a development artifact from modified source, not a fixed release artifact.' }
Write-Host 'Build MyCodex next to copy this package into its runtime/auth-core directory.'
