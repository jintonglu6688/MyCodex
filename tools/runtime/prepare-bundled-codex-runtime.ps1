param(
    [string]$Version = "latest",
    [ValidateSet("x86_64-pc-windows-msvc", "aarch64-pc-windows-msvc")]
    [string]$TargetTriple = "x86_64-pc-windows-msvc",
    [string]$DestinationRoot,
    [string]$RegistryBaseUrl = "https://registry.npmjs.org",
    # Retain the verified archive for publication and return its metadata.
    [string]$ArchiveDirectory,
    [switch]$PruneOldVersions
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = $repoRoot
}

function Assert-UnlinkedPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Linked Codex runtime path is unsupported: $current"
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Assert-UnlinkedTree([string]$Root) {
    Assert-UnlinkedPath $Root
    if (Test-Path -LiteralPath $Root -PathType Container) {
        foreach ($entry in Get-ChildItem -LiteralPath $Root -Force) {
            Assert-UnlinkedPath $entry.FullName
            if ($entry.PSIsContainer) { Assert-UnlinkedTree $entry.FullName }
        }
    }
}

$destinationRootAbsolute = [IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\')
Assert-UnlinkedPath $destinationRootAbsolute
if ($PruneOldVersions -and -not [string]::Equals($destinationRootAbsolute, $repoRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Old runtime cleanup is limited to this repository.'
}
$runtimeRoot = Join-Path $destinationRootAbsolute 'runtime\codex'
Assert-UnlinkedPath $runtimeRoot

function Get-PackageSuffix {
    param([string]$TargetTriple)

    if ($TargetTriple -eq "aarch64-pc-windows-msvc") {
        return "win32-arm64"
    }

    return "win32-x64"
}

function Get-MetadataString {
    param(
        [object]$Metadata,
        [string]$Name
    )

    if ($null -eq $Metadata) {
        return $null
    }

    $property = $Metadata.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return [string]$property.Value
}

function Get-MetadataObject {
    param(
        [object]$Metadata,
        [string]$Name
    )

    if ($null -eq $Metadata) {
        return $null
    }

    $property = $Metadata.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Invoke-CodexMetadataRequest {
    param([string]$Url)

    try { return Invoke-RestMethod -Uri $Url -TimeoutSec 4 -UseBasicParsing }
    catch [System.Net.WebException] {
        if (-not $Url.StartsWith('https://registry.npmjs.org/', [StringComparison]::Ordinal)) { throw }
        $mirrorUrl = 'https://registry.npmmirror.com/' + $Url.Substring('https://registry.npmjs.org/'.Length)
        return Invoke-RestMethod -Uri $mirrorUrl -TimeoutSec 4 -UseBasicParsing
    }
}

function Resolve-CodexVersion {
    param(
        [string]$RequestedVersion,
        [string]$RegistryBase
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion) -and
        -not [string]::Equals($RequestedVersion.Trim(), "latest", [StringComparison]::OrdinalIgnoreCase)) {
        return $RequestedVersion.Trim()
    }

    $latestMetadataUrl = "$RegistryBase/%40openai%2Fcodex/latest"
    Write-Host "Fetching latest Codex runtime metadata: $latestMetadataUrl"
    $latestMetadata = Invoke-CodexMetadataRequest -Url $latestMetadataUrl
    $latestVersion = Get-MetadataString -Metadata $latestMetadata -Name "version"
    if ([string]::IsNullOrWhiteSpace($latestVersion)) {
        throw "Codex runtime metadata did not include the latest version."
    }

    if ($latestVersion.Trim() -notmatch '^\d+\.\d+\.\d+$') {
        throw 'Latest Codex runtime metadata must name a stable version.'
    }
    return $latestVersion.Trim()
}

function Get-PlatformPackageMetadata {
    param(
        [string]$ResolvedVersion,
        [string]$PackageSuffix,
        [string]$RegistryBase
    )

    $platformPackageVersionTag = "$ResolvedVersion-$PackageSuffix"
    $candidates = @(
        @{
            MetadataUrl = "$RegistryBase/%40openai%2Fcodex/$platformPackageVersionTag"
            ExpectedFileName = "codex-$platformPackageVersionTag.tgz"
        },
        @{
            MetadataUrl = "$RegistryBase/%40openai%2Fcodex/$ResolvedVersion"
            ExpectedFileName = "codex-$ResolvedVersion.tgz"
        }
    )

    foreach ($candidate in $candidates) {
        try {
            Write-Host "Fetching Codex runtime metadata: $($candidate.MetadataUrl)"
            $metadata = Invoke-CodexMetadataRequest -Url $candidate.MetadataUrl
            $dist = Get-MetadataObject -Metadata $metadata -Name "dist"
            $tarballUrl = Get-MetadataString -Metadata $dist -Name "tarball"
            if ([string]::IsNullOrWhiteSpace($tarballUrl)) {
                throw "Codex runtime metadata did not include dist.tarball."
            }

            $tarballFileName = [IO.Path]::GetFileName(([Uri]$tarballUrl).AbsolutePath)
            if ($tarballFileName -ne $candidate.ExpectedFileName) {
                throw "Unexpected Codex runtime package name '$tarballFileName'. Expected '$($candidate.ExpectedFileName)'."
            }

            return [PSCustomObject]@{
                TarballUrl = $tarballUrl
                Integrity = Get-MetadataString -Metadata $dist -Name "integrity"
                ShaSum = Get-MetadataString -Metadata $dist -Name "shasum"
                ExpectedFileName = $candidate.ExpectedFileName
            }
        } catch [System.Net.WebException] {
            $response = $_.Exception.Response
            if ($null -eq $response -or $response.StatusCode -ne [System.Net.HttpStatusCode]::NotFound) {
                throw
            }
        }
    }

    throw "Codex runtime metadata was not found for version $ResolvedVersion."
}

function Convert-HexToBytes {
    param([string]$Hex)

    if ([string]::IsNullOrWhiteSpace($Hex) -or ($Hex.Length % 2) -ne 0) {
        return $null
    }

    $bytes = New-Object byte[] ($Hex.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($Hex.Substring($i * 2, 2), 16)
    }

    return $bytes
}

function New-HashAlgorithm {
    param([string]$Algorithm)

    switch ($Algorithm.ToUpperInvariant()) {
        "SHA1" { return [System.Security.Cryptography.SHA1]::Create() }
        "SHA256" { return [System.Security.Cryptography.SHA256]::Create() }
        "SHA384" { return [System.Security.Cryptography.SHA384]::Create() }
        "SHA512" { return [System.Security.Cryptography.SHA512]::Create() }
        default { throw "Unsupported hash algorithm: $Algorithm" }
    }
}

function Get-HashBytes {
    param(
        [string]$FilePath,
        [string]$Algorithm
    )

    $hashAlgorithm = New-HashAlgorithm -Algorithm $Algorithm
    try {
        $stream = [System.IO.File]::Open($FilePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        try {
            return $hashAlgorithm.ComputeHash($stream)
        } finally {
            $stream.Dispose()
        }
    } finally {
        $hashAlgorithm.Dispose()
    }
}

function Convert-BytesToHex {
    param([byte[]]$Bytes)

    if ($null -eq $Bytes) {
        return $null
    }

    $builder = New-Object System.Text.StringBuilder ($Bytes.Length * 2)
    foreach ($byte in $Bytes) {
        [void]$builder.AppendFormat("{0:x2}", $byte)
    }

    return $builder.ToString()
}

function Test-SriIntegrity {
    param(
        [string]$FilePath,
        [string]$Integrity
    )

    if ([string]::IsNullOrWhiteSpace($Integrity)) {
        return $false
    }

    $hasStrongHash = $false
    $weakMatch = $false
    foreach ($token in $Integrity.Split(" ", [StringSplitOptions]::RemoveEmptyEntries)) {
        $separator = $token.IndexOf("-")
        if ($separator -le 0 -or $separator -ge ($token.Length - 1)) {
            continue
        }

        $algorithm = $token.Substring(0, $separator).ToUpperInvariant()
        $expected = $token.Substring($separator + 1)
        if ($algorithm -notin @("SHA1", "SHA256", "SHA384", "SHA512")) {
            continue
        }

        $bytes = Get-HashBytes -FilePath $FilePath -Algorithm $algorithm
        $actual = [Convert]::ToBase64String($bytes)
        $strong = $algorithm -ne 'SHA1'
        $hasStrongHash = $hasStrongHash -or $strong
        if ($actual -ceq $expected) {
            if ($strong) { return $true }
            $weakMatch = $true
        }
    }

    return $weakMatch -and -not $hasStrongHash
}

function Test-Sha1 {
    param(
        [string]$FilePath,
        [string]$ShaSum
    )

    if ([string]::IsNullOrWhiteSpace($ShaSum)) {
        return $false
    }

    $hashBytes = Get-HashBytes -FilePath $FilePath -Algorithm "SHA1"
    $hashHex = Convert-BytesToHex -Bytes $hashBytes
    return $hashHex -ieq $ShaSum
}

function Resolve-CodexExecutablePath {
    param(
        [string]$PackageRoot,
        [string]$TargetTriple
    )

    $currentPath = Join-Path $PackageRoot "vendor\$TargetTriple\bin\codex.exe"
    if (Test-Path $currentPath) {
        return $currentPath
    }

    return Join-Path $PackageRoot "vendor\$TargetTriple\codex\codex.exe"
}

$packageSuffix = Get-PackageSuffix $TargetTriple
$registryBase = $RegistryBaseUrl.TrimEnd("/")
$resolvedVersion = Resolve-CodexVersion -RequestedVersion $Version -RegistryBase $registryBase
if ($resolvedVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw 'Invalid Codex runtime version.'
}
$packageMetadata = Get-PlatformPackageMetadata -ResolvedVersion $resolvedVersion -PackageSuffix $packageSuffix -RegistryBase $registryBase
$tarballUrl = $packageMetadata.TarballUrl
$integrity = $packageMetadata.Integrity
$shaSum = $packageMetadata.ShaSum
$expectedFileName = $packageMetadata.ExpectedFileName

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("MyCodex-CodexRuntime-" + [Guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $tempRoot $expectedFileName
$extractRoot = Join-Path $tempRoot "extract"
$destinationPath = Join-Path $destinationRootAbsolute "runtime\codex\$resolvedVersion\$TargetTriple"
if (-not [IO.Path]::GetFullPath($destinationPath).StartsWith($runtimeRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Runtime destination is outside runtime/codex.'
}
Assert-UnlinkedTree $destinationPath
if (-not [string]::IsNullOrWhiteSpace($ArchiveDirectory)) {
    $archiveDirectoryAbsolute = [IO.Path]::GetFullPath($ArchiveDirectory).TrimEnd('\')
    if ([string]::Equals($archiveDirectoryAbsolute, $runtimeRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $archiveDirectoryAbsolute.StartsWith($runtimeRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Publication archives must be outside runtime/codex.'
    }
}

# Validate every cleanup candidate before modifying anything. Only repository runtime/codex is pruned.
$oldVersions = @()
if ($PruneOldVersions -and (Test-Path -LiteralPath $runtimeRoot)) {
    $oldVersions = @(Get-ChildItem -LiteralPath $runtimeRoot -Directory -Force | Where-Object { $_.Name -ne $resolvedVersion })
    foreach ($old in $oldVersions) { Assert-UnlinkedTree $old.FullName }
}

try {
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    New-Item -ItemType Directory -Path $extractRoot | Out-Null

    $downloadUrls = @($tarballUrl)
    if ($tarballUrl.StartsWith('https://registry.npmjs.org/', [StringComparison]::Ordinal)) {
        $downloadUrls = @(('https://registry.npmmirror.com/' + $tarballUrl.Substring('https://registry.npmjs.org/'.Length)), $tarballUrl)
    } elseif ($tarballUrl.StartsWith('https://registry.npmmirror.com/', [StringComparison]::Ordinal)) {
        $downloadUrls += 'https://registry.npmjs.org/' + $tarballUrl.Substring('https://registry.npmmirror.com/'.Length)
    }
    $downloadUrl = $null
    $oldProgressPreference = $ProgressPreference
    try {
        $ProgressPreference = 'SilentlyContinue'
        for ($downloadIndex = 0; $downloadIndex -lt $downloadUrls.Count; $downloadIndex++) {
            $candidateUrl = $downloadUrls[$downloadIndex]
            try {
                Write-Host "Downloading Codex runtime: $candidateUrl"
                Invoke-WebRequest -Uri $candidateUrl -OutFile $archivePath -TimeoutSec 900 -UseBasicParsing
                Write-Host "Verifying Codex runtime package integrity..."
                $valid = Test-SriIntegrity -FilePath $archivePath -Integrity $integrity
                # A matching SHA-1 must never rescue a failed stronger upstream checksum.
                if (-not $valid -and $integrity -notmatch '(?i)(?:^|\s)sha(?:256|384|512)-') {
                    $valid = Test-Sha1 -FilePath $archivePath -ShaSum $shaSum
                }
                if (-not $valid) { throw 'Codex runtime package integrity check failed.' }
                $downloadUrl = $candidateUrl
                break
            } catch {
                if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
                if ($downloadIndex -eq $downloadUrls.Count - 1) { throw }
                Write-Host 'Mirror download unavailable or invalid; trying the same official package.'
            }
        }
    } finally { $ProgressPreference = $oldProgressPreference }

    Write-Host "Extracting Codex runtime..."
    tar.exe -xzf $archivePath -C $extractRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to extract Codex runtime package."
    }

    $packageRoot = Join-Path $extractRoot "package"
    $codexExe = Resolve-CodexExecutablePath -PackageRoot $packageRoot -TargetTriple $TargetTriple
    if (-not (Test-Path $codexExe)) {
        throw "Codex runtime package did not contain $codexExe."
    }
    Assert-UnlinkedTree $packageRoot

    $package = $null
    if (-not [string]::IsNullOrWhiteSpace($ArchiveDirectory)) {
        $archiveDestination = Join-Path ([IO.Path]::GetFullPath($ArchiveDirectory)) "$resolvedVersion\$expectedFileName"
        Assert-UnlinkedPath $archiveDestination
        New-Item -ItemType Directory -Path (Split-Path -Parent $archiveDestination) -Force | Out-Null
        Copy-Item -LiteralPath $archivePath -Destination $archiveDestination -Force
        $package = [pscustomobject]@{
            Version = $resolvedVersion
            Path = $archiveDestination
            FileName = $expectedFileName
            Size = (Get-Item -LiteralPath $archiveDestination).Length
            Sha256 = (Get-FileHash -LiteralPath $archiveDestination -Algorithm SHA256).Hash.ToLowerInvariant()
            RuntimePath = $destinationPath
            DownloadUrl = $downloadUrl
        }
    }

    if (Test-Path $destinationPath) {
        Assert-UnlinkedTree $destinationPath
        Remove-Item -LiteralPath $destinationPath -Recurse -Force
    }

    $destinationParent = Split-Path -Parent $destinationPath
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    Move-Item -LiteralPath $packageRoot -Destination $destinationPath

    foreach ($old in $oldVersions) {
        $oldPath = [IO.Path]::GetFullPath($old.FullName)
        if (-not $oldPath.StartsWith($runtimeRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to delete outside repository runtime/codex.'
        }
        Assert-UnlinkedTree $oldPath
        Write-Host "Removing old repository Codex runtime: $($old.Name)"
        Remove-Item -LiteralPath $oldPath -Recurse -Force
    }

    Write-Host "Bundled Codex runtime prepared: $destinationPath"
    if ($null -ne $package) { $package }
} finally {
    if (Test-Path $tempRoot) {
        Assert-UnlinkedPath $tempRoot
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
