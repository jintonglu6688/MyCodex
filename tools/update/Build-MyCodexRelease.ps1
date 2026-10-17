param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [string]$PackageBaseUrl,

    [string]$Channel = "stable",

    [string]$ReleaseNotesZhCn = "",

    [string]$ReleaseNotesEnUs = "",

    [string]$BundledCodexVersion = "latest",

    [string]$StandardRuntimePath,

    [string]$StandardRuntimeVersion,

    [string]$AssetUrlSuffix = "/download",

    [switch]$EscapeNonAsciiManifest,

    [switch]$SkipInstallerBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$versionPropsPath = Join-Path $repoRoot "Version.props"
$installerScript = Join-Path $repoRoot "tools\installer\build-installer.ps1"
$manifestScript = Join-Path $repoRoot "tools\update\New-MyCodexUpdateManifest.ps1"

function Get-AppVersion {
    param([string]$Path)

    $content = Get-Content -LiteralPath $Path -Raw
    $match = [regex]::Match($content, "<Version>([^<]+)</Version>")
    if (-not $match.Success) {
        throw "Unable to read <Version> from $Path"
    }

    return $match.Groups[1].Value.Trim()
}

$version = Get-AppVersion -Path $versionPropsPath
$installerPath = Join-Path $repoRoot ("artifacts\installer\MyCodex_v" + $version + "_Setup.exe")

if (-not $SkipInstallerBuild) {
    & $installerScript `
        -Configuration $Configuration `
        -BundledCodexVersion $BundledCodexVersion
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed."
    }
}

if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer was not found: $installerPath"
}
. (Join-Path $repoRoot 'tools\authentication-center\Test-AuthenticationHostPackage.ps1')
Test-AuthenticationInstallerReceipt -InstallerPath $installerPath

if ([string]::IsNullOrWhiteSpace($PackageBaseUrl)) {
    $PackageBaseUrl = "https://api.gitcode.com/api/v5/repos/<owner>/<repo>/releases/app-v$version/attach_files/"
}

& $manifestScript `
    -Version $version `
    -InstallerPath $installerPath `
    -PackageBaseUrl $PackageBaseUrl `
    -Channel $Channel `
    -ReleaseNotesZhCn $ReleaseNotesZhCn `
    -ReleaseNotesEnUs $ReleaseNotesEnUs `
    -StandardRuntimePath $StandardRuntimePath `
    -StandardRuntimeVersion $StandardRuntimeVersion `
    -AssetUrlSuffix $AssetUrlSuffix `
    -EscapeNonAscii:$EscapeNonAsciiManifest

if ($LASTEXITCODE -ne 0) {
    throw "Manifest generation failed."
}

Write-Host ""
Write-Host "Release files:"
Write-Host "  Installer: $installerPath"
Write-Host "  Manifest:  $(Join-Path $repoRoot "artifacts\update\$Channel.json")"
