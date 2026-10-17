param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationPath,
    [string]$CacheRoot,
    [string]$ExpectedExecutableSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$tag = (Get-Content -LiteralPath (Join-Path $PSScriptRoot "relay-runtime.version") -TotalCount 1).Trim()
if ($tag -notmatch '^relay-v\d+\.\d+\.\d+$') {
    throw "Invalid Relay runtime tag: $tag"
}

$version = $tag.Substring("relay-v".Length)
$archiveName = "mycodex-relay-$version-windows-x64.zip"
$releaseBase = "https://github.com/jintonglu6688/MyCodex-relay/releases/download/$tag"
if ([string]::IsNullOrWhiteSpace($ExpectedExecutableSha256)) {
    $ExpectedExecutableSha256 = [IO.File]::ReadAllText(
        (Join-Path $PSScriptRoot "relay-runtime.sha256")).Trim()
}
if ($ExpectedExecutableSha256 -notmatch '^[0-9a-fA-F]{64}$') {
    throw "Invalid pinned Relay executable SHA-256."
}
$destinationFullPath = [IO.Path]::GetFullPath($DestinationPath)
if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    $CacheRoot = Join-Path $repoRoot "runtime\relay"
}
$cacheDirectory = Join-Path ([IO.Path]::GetFullPath($CacheRoot)) "$tag\windows-x64"
$cacheExecutable = Join-Path $cacheDirectory "mycodex-relay.exe"

if (Test-Path -LiteralPath $cacheExecutable -PathType Leaf) {
    if ([string]::Equals(
        $ExpectedExecutableSha256,
        (Get-FileHash -LiteralPath $cacheExecutable -Algorithm SHA256).Hash,
        [StringComparison]::OrdinalIgnoreCase)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $destinationFullPath) -Force | Out-Null
        Copy-Item -LiteralPath $cacheExecutable -Destination $destinationFullPath -Force
        Write-Host "Using verified cached Relay $tag"
        return
    }
}

$tempRoot = Join-Path $repoRoot "temp\relay-runtime"
$workRoot = Join-Path $tempRoot ([Guid]::NewGuid().ToString("N"))
$resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot).TrimEnd('\') + '\'
$resolvedWorkRoot = [IO.Path]::GetFullPath($workRoot)
if (-not $resolvedWorkRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe Relay work directory: $workRoot"
}

New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
try {
    $archivePath = Join-Path $workRoot $archiveName
    $checksumPath = Join-Path $workRoot "SHA256SUMS.txt"
    Invoke-WebRequest -Uri "$releaseBase/$archiveName" -OutFile $archivePath -UseBasicParsing
    Invoke-WebRequest -Uri "$releaseBase/SHA256SUMS.txt" -OutFile $checksumPath -UseBasicParsing

    $checksumPattern = '^([0-9a-fA-F]{64})  ' + [regex]::Escape($archiveName) + '$'
    $checksumRows = @(Get-Content -LiteralPath $checksumPath | Where-Object { $_ -match $checksumPattern })
    if ($checksumRows.Count -ne 1) {
        throw "Relay release checksums do not identify $archiveName exactly once."
    }
    $expectedHash = [regex]::Match($checksumRows[0], $checksumPattern).Groups[1].Value
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($expectedHash, $actualHash, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Relay archive checksum mismatch for $archiveName."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entry = $archive.GetEntry("windows-x64/mycodex-relay.exe")
        if ($null -eq $entry) {
            $entry = $archive.GetEntry("windows-x64\mycodex-relay.exe")
        }
        if ($null -eq $entry -or $entry.Length -eq 0) {
            throw "Relay archive is missing windows-x64/mycodex-relay.exe."
        }
        $extractedPath = Join-Path $workRoot "mycodex-relay.exe"
        $source = $entry.Open()
        $destination = [IO.File]::Create($extractedPath)
        try {
            $source.CopyTo($destination)
        } finally {
            $destination.Dispose()
            $source.Dispose()
        }
    } finally {
        $archive.Dispose()
    }

    if (-not [string]::Equals(
        $ExpectedExecutableSha256,
        (Get-FileHash -LiteralPath $extractedPath -Algorithm SHA256).Hash,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Relay executable checksum mismatch for $tag."
    }
    New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
    Copy-Item -LiteralPath $extractedPath -Destination $cacheExecutable -Force
    New-Item -ItemType Directory -Path (Split-Path -Parent $destinationFullPath) -Force | Out-Null
    Copy-Item -LiteralPath $cacheExecutable -Destination $destinationFullPath -Force
    Write-Host "Verified Relay ${tag}: $destinationFullPath"
} finally {
    Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}
