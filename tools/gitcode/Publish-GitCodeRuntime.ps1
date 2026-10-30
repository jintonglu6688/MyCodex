param(
    [ValidateSet("Bundled")]
    [string]$Source = "Bundled",

    [string]$Version,

    [string]$Owner = "gcw_SpGZ48lW",

    [string]$Repo = "mycodex-updates",

    [string]$RepoUrl,

    [string]$Token = $env:GITCODE_TOKEN,

    [string]$RegistryBaseUrl = "https://registry.npmjs.org",

    [ValidateSet("x86_64-pc-windows-msvc", "aarch64-pc-windows-msvc")]
    [string[]]$TargetTriple = @("x86_64-pc-windows-msvc"),

    [string]$ManifestPath,

    [switch]$SkipDistributionRepoPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$gitCodeApiBase = "https://api.gitcode.com/api/v5"
$distributionScript = Join-Path $repoRoot "tools\gitcode\Publish-GitCodeDistributionRepo.ps1"
$prepareScript = Join-Path $repoRoot "tools\runtime\prepare-bundled-codex-runtime.ps1"
$script:CachedGitCodeToken = $null
. (Join-Path $repoRoot "tools\gitcode\GitCodePublishing.ps1")

if ([string]::IsNullOrWhiteSpace($RepoUrl)) {
    $RepoUrl = "https://gitcode.com/$Owner/$Repo.git"
}

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $repoRoot "updates\stable.json"
}

function ConvertTo-AsciiJson {
    param([string]$Json)

    $builder = [System.Text.StringBuilder]::new($Json.Length)
    foreach ($character in $Json.ToCharArray()) {
        $code = [int][char]$character
        if ($code -le 127) {
            [void]$builder.Append($character)
        }
        else {
            [void]$builder.Append(("\u{0:x4}" -f $code))
        }
    }

    return $builder.ToString()
}

function Write-Manifest {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Manifest,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $json = ConvertTo-AsciiJson -Json ($Manifest | ConvertTo-Json -Depth 12)
    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $json.Replace("`r`n", "`n") + "`n", $encoding)
}

function Add-OrUpdateRuntimePackage {
    param(
        [object]$Manifest,
        [string]$RuntimeSource,
        [string]$RuntimeVersion,
        [string]$TargetTriple,
        [object]$Package
    )

    if ($null -eq $Manifest.runtimes) {
        $Manifest | Add-Member -NotePropertyName runtimes -NotePropertyValue @()
    }

    $items = @($Manifest.runtimes)
    $match = $items |
        Where-Object { $_.source -eq $RuntimeSource -and $_.targetTriple -eq $TargetTriple } |
        Select-Object -First 1
    if ($null -eq $match) {
        $match = [pscustomobject]@{
            source = $RuntimeSource
            version = $RuntimeVersion
            targetTriple = $TargetTriple
            packageFormat = "npm-tgz"
            asset = $null
        }
        $Manifest.runtimes = @($items + $match)
    }

    $match.version = $RuntimeVersion
    $match.packageFormat = "npm-tgz"
    $asset = [pscustomobject]@{
        url = $Package.DownloadUrl
        fileName = $Package.FileName
        sha256 = $Package.Sha256
        size = $Package.Size
    }
    if ($match.PSObject.Properties.Name -contains "asset") {
        $match.asset = $asset
    }
    else {
        $match | Add-Member -NotePropertyName asset -NotePropertyValue $asset
    }
}

$sourceRemotes = @(git -C $repoRoot remote)
if ($sourceRemotes -contains "gitcode") {
    throw "Source repository still has a 'gitcode' remote. Remove it first to avoid publishing source code to GitCode."
}

# Read credentials and the existing manifest before updating local files.
$Token = Get-GitCodeToken
$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$version = if ([string]::IsNullOrWhiteSpace($Version)) { 'latest' } else { $Version.Trim() }
$targetTriples = @($TargetTriple | Select-Object -Unique)
if ($targetTriples.Count -eq 0) { throw 'At least one runtime architecture is required.' }
$runtimeArtifactDirectory = Join-Path $repoRoot "artifacts\runtime\$Source"

$packages = @{}
for ($index = 0; $index -lt $targetTriples.Count; $index++) {
    $targetTripleKey = [string]$targetTriples[$index]
    $package = & $prepareScript `
        -Version $version `
        -TargetTriple $targetTripleKey `
        -RegistryBaseUrl $RegistryBaseUrl `
        -DestinationRoot $repoRoot `
        -ArchiveDirectory $runtimeArtifactDirectory `
        -PruneOldVersions:($index -eq $targetTriples.Count - 1)
    # Resolve latest once; all architectures and the manifest use that same version.
    $version = $package.Version
    $packages[$targetTripleKey] = $package
}
# Old clients still read the small manifest; verified npm archives are downloaded directly.
$manifest.generatedAt = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
foreach ($targetTriple in $targetTriples) {
    $targetTripleKey = [string]$targetTriple
    if (-not $packages.ContainsKey($targetTripleKey) -or $null -eq $packages[$targetTripleKey]) {
        throw "Runtime package was not prepared for $targetTripleKey."
    }

    Add-OrUpdateRuntimePackage `
        -Manifest $manifest `
        -RuntimeSource $Source `
        -RuntimeVersion $version `
        -TargetTriple $targetTripleKey `
        -Package ($packages[$targetTripleKey])
}
Write-Manifest -Manifest $manifest -Path $ManifestPath

if (-not $SkipDistributionRepoPublish) {
    & $distributionScript `
        -RepoUrl $RepoUrl `
        -CommitMessage "Publish MyCodex $Source runtime $version" `
        -GitUsername $Owner `
        -GitPassword (Get-GitCodeToken)
    if ($LASTEXITCODE -ne 0) {
        throw "Distribution repository publish failed."
    }
}

Remove-RemoteTagIfExists -TagName "updates-stable"
New-GitCodeRelease `
    -TagName "updates-stable" `
    -Name "Stable Update Manifest" `
    -Body "Stable manifest pointer for MyCodex update checks."
Send-GitCodeReleaseFile -TagName "updates-stable" -Path $ManifestPath

foreach ($targetTriple in $targetTriples) {
    $package = $packages[[string]$targetTriple]
    $url = $package.DownloadUrl
    Assert-GitCodePublicDownloadSize -Url $url -ExpectedSize ([long]$package.Size) -Name "Runtime package $targetTriple"
}

Write-Host ""
Write-Host "Legacy runtime manifest published:"
Write-Host "  Source:  $Source"
Write-Host "  Version: $version"
Write-Host "  Packages: verified npm URLs (no runtime attachment upload)"
