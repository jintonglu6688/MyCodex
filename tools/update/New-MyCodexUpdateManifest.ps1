param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [Parameter(Mandatory = $true)]
    [string]$PackageBaseUrl,

    [string]$Channel = "stable",

    [string]$OutputPath,

    [string]$ReleaseNotesZhCn = "",

    [string]$ReleaseNotesEnUs = "",

    [string]$StandardRuntimePath,

    [string]$StandardRuntimeVersion,

    [string]$TargetTriple = "x86_64-pc-windows-msvc",

    [string]$AssetUrlSuffix = "",

    [switch]$PreserveExistingRuntimes,

    [switch]$EscapeNonAscii
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts\update\$Channel.json"
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

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Value, $encoding)
}

function Get-FileAsset {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $item = Get-Item -LiteralPath $resolvedPath
    return [ordered]@{
        url = $item.Name + $AssetUrlSuffix
        fileName = $item.Name
        sha256 = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash.ToLowerInvariant()
        size = $item.Length
    }
}

function Add-RuntimePackage {
    param(
        [System.Collections.ArrayList]$Runtimes,
        [string]$Source,
        [string]$RuntimeVersion,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    if ([string]::IsNullOrWhiteSpace($RuntimeVersion)) {
        throw "Runtime version is required when runtime path is provided for $Source."
    }

    [void]$Runtimes.Add([ordered]@{
        source = $Source
        version = $RuntimeVersion
        targetTriple = $TargetTriple
        packageFormat = "npm-tgz"
        asset = Get-FileAsset -Path $Path
    })
}

function ConvertTo-WindowsLineEndings {
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return $Value
    }

    return ($Value.Trim() -replace "`r`n", "`n" -replace "`r", "`n" -replace "`n", "`r`n")
}

. (Join-Path $repoRoot 'tools\authentication-center\Test-AuthenticationHostPackage.ps1')
Test-AuthenticationInstallerReceipt -InstallerPath $InstallerPath
$installerAsset = Get-FileAsset -Path $InstallerPath
$runtimes = [System.Collections.ArrayList]::new()
Add-RuntimePackage -Runtimes $runtimes -Source "Bundled" -RuntimeVersion $StandardRuntimeVersion -Path $StandardRuntimePath
if ($PreserveExistingRuntimes -and $runtimes.Count -eq 0 -and (Test-Path -LiteralPath $OutputPath)) {
    $existingManifest = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json
    if ($existingManifest -ne $null -and $existingManifest.runtimes -ne $null) {
        foreach ($runtime in @($existingManifest.runtimes)) {
            [void]$runtimes.Add($runtime)
        }
    }
}

$manifest = [ordered]@{
    schemaVersion = "1"
    channel = $Channel
    generatedAt = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
    packageBaseUrl = $PackageBaseUrl.TrimEnd("/") + "/"
    app = [ordered]@{
        version = $Version
        releaseDate = [DateTime]::UtcNow.ToString("yyyy-MM-dd")
        releaseNotes = [ordered]@{
            "zh-CN" = ConvertTo-WindowsLineEndings $ReleaseNotesZhCn
            "en-US" = ConvertTo-WindowsLineEndings $ReleaseNotesEnUs
        }
        asset = $installerAsset
    }
    runtimes = @($runtimes)
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$json = $manifest | ConvertTo-Json -Depth 8
if ($EscapeNonAscii) {
    $json = ConvertTo-AsciiJson -Json $json
}

Write-Utf8NoBom -Path $OutputPath -Value ($json + [Environment]::NewLine)

Write-Host "Manifest created:"
Write-Host $OutputPath
