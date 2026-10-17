$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$scriptPath = Join-Path $repoRoot "tools\runtime\prepare-relay-runtime.ps1"
$tag = (Get-Content (Join-Path $repoRoot "tools\runtime\relay-runtime.version") -TotalCount 1).Trim()
$version = $tag.Substring("relay-v".Length)
$archiveName = "mycodex-relay-$version-windows-x64.zip"
$fixtureRoot = Join-Path $repoRoot ("temp\relay-runtime-test-" + [Guid]::NewGuid().ToString("N"))
$payloadRoot = Join-Path $fixtureRoot "payload"
$fixtureArchivePath = Join-Path $fixtureRoot $archiveName
$fixtureChecksumPath = Join-Path $fixtureRoot "SHA256SUMS.txt"
$fixtureCacheRoot = Join-Path $fixtureRoot "cache"
$cachedExecutable = Join-Path $fixtureCacheRoot "$tag\windows-x64\mycodex-relay.exe"
$destinationPath = Join-Path $fixtureRoot "output\mycodex-relay.exe"
$global:relayFixtureDownloadCount = 0
$global:relayFixtureDownloadsAllowed = $true

function Invoke-WebRequest {
    param([string]$Uri, [string]$OutFile, [switch]$UseBasicParsing)

    if (-not $global:relayFixtureDownloadsAllowed) {
        throw "Cached Relay unexpectedly requested a download."
    }
    $global:relayFixtureDownloadCount++
    $source = if ($Uri.EndsWith("/SHA256SUMS.txt", [StringComparison]::Ordinal)) {
        $fixtureChecksumPath
    } else {
        if (-not $Uri.EndsWith("/$archiveName", [StringComparison]::Ordinal)) {
            throw "Unexpected Relay download: $Uri"
        }
        $fixtureArchivePath
    }
    Copy-Item -LiteralPath $source -Destination $OutFile
}

New-Item -ItemType Directory -Path (Join-Path $payloadRoot "windows-x64") -Force | Out-Null
try {
    [IO.File]::WriteAllText((Join-Path $payloadRoot "windows-x64\mycodex-relay.exe"), "relay fixture")
    $fixtureExecutableHash = (Get-FileHash -LiteralPath (Join-Path $payloadRoot "windows-x64\mycodex-relay.exe") -Algorithm SHA256).Hash
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payloadRoot, $fixtureArchivePath)
    $hash = (Get-FileHash -LiteralPath $fixtureArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($fixtureChecksumPath, "$hash  $archiveName")

    & $scriptPath -DestinationPath $destinationPath -CacheRoot $fixtureCacheRoot -ExpectedExecutableSha256 $fixtureExecutableHash
    if ([IO.File]::ReadAllText($destinationPath) -ne "relay fixture") {
        throw "Verified Relay payload was not copied."
    }
    if ($global:relayFixtureDownloadCount -ne 2 -or
        -not (Test-Path -LiteralPath $cachedExecutable -PathType Leaf)) {
        throw "Verified Relay was not cached."
    }

    [IO.File]::WriteAllText($destinationPath, "stale")
    $global:relayFixtureDownloadsAllowed = $false
    & $scriptPath -DestinationPath $destinationPath -CacheRoot $fixtureCacheRoot -ExpectedExecutableSha256 $fixtureExecutableHash
    if ($global:relayFixtureDownloadCount -ne 2 -or
        [IO.File]::ReadAllText($destinationPath) -ne "relay fixture") {
        throw "Cached Relay was not reused."
    }

    $global:relayFixtureDownloadsAllowed = $true
    [IO.File]::WriteAllText($cachedExecutable, "corrupt")
    & $scriptPath -DestinationPath $destinationPath -CacheRoot $fixtureCacheRoot -ExpectedExecutableSha256 $fixtureExecutableHash
    if ($global:relayFixtureDownloadCount -ne 4 -or
        [IO.File]::ReadAllText($cachedExecutable) -ne "relay fixture") {
        throw "Damaged Relay cache was not refreshed."
    }

    [IO.File]::WriteAllText($cachedExecutable, "corrupt")
    try {
        & $scriptPath -DestinationPath $destinationPath -CacheRoot $fixtureCacheRoot -ExpectedExecutableSha256 ("0" * 64)
        throw "Relay executable with the wrong pinned hash was accepted."
    } catch {
        if ($_.Exception.Message -notlike "*executable checksum mismatch*") {
            throw
        }
    }
    [IO.File]::WriteAllText($fixtureChecksumPath, ("0" * 64) + "  $archiveName")
    try {
        & $scriptPath -DestinationPath $destinationPath -CacheRoot $fixtureCacheRoot -ExpectedExecutableSha256 $fixtureExecutableHash
        throw "Relay checksum mismatch was accepted."
    } catch {
        if ($_.Exception.Message -notlike "*checksum mismatch*") {
            throw
        }
    }
    if ([IO.File]::ReadAllText($destinationPath) -ne "relay fixture") {
        throw "Failed download changed the existing Relay payload."
    }
    Write-Host "Relay preparation checks passed."
} finally {
    $resolvedRoot = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    if ([IO.Path]::GetFullPath($fixtureRoot).StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
