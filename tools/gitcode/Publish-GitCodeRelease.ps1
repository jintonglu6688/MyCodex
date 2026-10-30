param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [string]$Owner = "gcw_SpGZ48lW",

    [string]$Repo = "mycodex-updates",

    [string]$RepoUrl,

    [string]$Token = $env:GITCODE_TOKEN,

    [string]$Channel = "stable",

    [string]$ReleaseNotesZhCn = "",

    [string]$ReleaseNotesEnUs = "",

    [string]$BundledCodexVersion = "latest",

    [string]$UniqueSuffix,

    [string]$StandardRuntimePath,

    [string]$StandardRuntimeVersion,

    [switch]$SkipInstallerBuild,

    [switch]$SkipStandardRuntimePublish,

    [switch]$SkipDistributionRepoPublish,

    [switch]$PrepareOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$versionPropsPath = Join-Path $repoRoot "Version.props"
$installerScript = Join-Path $repoRoot "tools\installer\build-installer.ps1"
$manifestScript = Join-Path $repoRoot "tools\update\New-MyCodexUpdateManifest.ps1"
$distributionScript = Join-Path $repoRoot "tools\gitcode\Publish-GitCodeDistributionRepo.ps1"
$runtimeScript = Join-Path $repoRoot "tools\gitcode\Publish-GitCodeRuntime.ps1"
$gitCodeApiBase = "https://api.gitcode.com/api/v5"
$script:CachedGitCodeToken = $null
. (Join-Path $repoRoot "tools\gitcode\GitCodePublishing.ps1")
. (Join-Path $repoRoot "tools\build\ScriptHelpers.ps1")

if ([string]::IsNullOrWhiteSpace($RepoUrl)) {
    $RepoUrl = "https://gitcode.com/$Owner/$Repo.git"
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = [Environment]::GetEnvironmentVariable("GITCODE_TOKEN", "User")
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = [Environment]::GetEnvironmentVariable("GITCODE_TOKEN", "Machine")
}

$sourceRemotes = @(git remote)
if ($sourceRemotes -contains "gitcode") {
    throw "Source repository still has a 'gitcode' remote. Remove it first to avoid publishing source code to GitCode."
}

$version = Get-AppVersion -Path $versionPropsPath
$releaseTag = "app-v$version"
$stableTag = "updates-$Channel"
$packageBaseUrl = "$gitCodeApiBase/repos/$Owner/$Repo/releases/$releaseTag/attach_files/"
$installerPath = Join-Path $repoRoot ("artifacts\installer\MyCodex_v" + $version + "_Setup.exe")

if ([string]::IsNullOrWhiteSpace($UniqueSuffix)) {
    $UniqueSuffix = [DateTime]::UtcNow.ToString("yyyyMMddHHmmss")
}

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

$installerDirectory = Split-Path -Parent $installerPath
. (Join-Path $repoRoot 'tools\authentication-center\Test-AuthenticationHostPackage.ps1')
Test-AuthenticationInstallerReceipt -InstallerPath $installerPath
$uniqueInstallerName = "MyCodex_v$version`_Setup_$UniqueSuffix.exe"
$uniqueInstallerPath = Join-Path $installerDirectory $uniqueInstallerName
Copy-Item -LiteralPath $installerPath -Destination $uniqueInstallerPath -Force
Copy-Item -LiteralPath ($installerPath + '.auth.json') -Destination ($uniqueInstallerPath + '.auth.json') -Force

if (-not $PrepareOnly -and -not $SkipStandardRuntimePublish -and
    [string]::IsNullOrWhiteSpace($StandardRuntimePath) -and
    [string]::IsNullOrWhiteSpace($StandardRuntimeVersion)) {
    $runtimePublishArguments = @{
        Owner = $Owner
        Repo = $Repo
        RepoUrl = $RepoUrl
        Token = Get-GitCodeToken
    }
    if (-not [string]::IsNullOrWhiteSpace($BundledCodexVersion) -and
        -not [string]::Equals($BundledCodexVersion.Trim(), "latest", [StringComparison]::OrdinalIgnoreCase)) {
        $runtimePublishArguments.Version = $BundledCodexVersion.Trim()
    }

    & $runtimeScript @runtimePublishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Standard runtime publish failed."
    }
}

$manifestPath = Join-Path $repoRoot "updates\$Channel.json"
& $manifestScript `
    -Version $version `
    -InstallerPath $uniqueInstallerPath `
    -PackageBaseUrl $packageBaseUrl `
    -Channel $Channel `
    -OutputPath $manifestPath `
    -ReleaseNotesZhCn $ReleaseNotesZhCn `
    -ReleaseNotesEnUs $ReleaseNotesEnUs `
    -StandardRuntimePath $StandardRuntimePath `
    -StandardRuntimeVersion $StandardRuntimeVersion `
    -AssetUrlSuffix "/download" `
    -PreserveExistingRuntimes `
    -EscapeNonAscii
if ($LASTEXITCODE -ne 0) {
    throw "Manifest generation failed."
}

if ($PrepareOnly) {
    Write-Host ""
    Write-Host "Release prepared without publishing:"
    Write-Host "  Version:   $version"
    Write-Host "  Installer: $uniqueInstallerPath"
    Write-Host "  Manifest:  $manifestPath"
    return
}

$gitCodeTokenForPublish = Get-GitCodeToken

if (-not $SkipDistributionRepoPublish) {
    & $distributionScript `
        -RepoUrl $RepoUrl `
        -CommitMessage "Publish MyCodex $version $Channel update manifest" `
        -GitUsername $Owner `
        -GitPassword $gitCodeTokenForPublish
    if ($LASTEXITCODE -ne 0) {
        throw "Distribution repository publish failed."
    }
}

Remove-RemoteTagIfExists -TagName $releaseTag
Remove-RemoteTagIfExists -TagName $stableTag

$releaseBody = "$ReleaseNotesZhCn`n`n$ReleaseNotesEnUs".Trim()
New-GitCodeRelease -TagName $releaseTag -Name "MyCodex $version" -Body $releaseBody
Send-GitCodeReleaseFile -TagName $releaseTag -Path $uniqueInstallerPath

New-GitCodeRelease -TagName $stableTag -Name "Stable Update Manifest" -Body "Stable manifest pointer for MyCodex update checks."
Send-GitCodeReleaseFile -TagName $stableTag -Path $manifestPath

$manifestUrl = "$gitCodeApiBase/repos/$Owner/$Repo/releases/$stableTag/attach_files/$Channel.json/download"
$manifest = Invoke-RestMethod -Uri $manifestUrl -TimeoutSec 120
if ($manifest.app.version -ne $version) {
    throw "Published manifest version mismatch. Expected $version, got $($manifest.app.version)."
}

$assetUrl = "$packageBaseUrl$($manifest.app.asset.url)"
Assert-GitCodePublicDownloadSize -Url $assetUrl -ExpectedSize ([long]$manifest.app.asset.size) -Name "Installer"

Write-Host ""
Write-Host "GitCode release published:"
Write-Host "  Version:  $version"
Write-Host "  Manifest: $manifestUrl"
Write-Host "  Asset:    $assetUrl"
