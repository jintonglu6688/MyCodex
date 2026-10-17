[CmdletBinding(DefaultParameterSetName = 'Create')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Create')]
    [string]$WindowsRepository,

    [Parameter(Mandatory = $true, ParameterSetName = 'Create')]
    [string]$RelayRepository,

    [Parameter(Mandatory = $true, ParameterSetName = 'Create')]
    [string]$AndroidRepository,

    [Parameter(Mandatory = $true, ParameterSetName = 'Create')]
    [string]$ExpectedWindowsHead,

    [Parameter(Mandatory = $true, ParameterSetName = 'Create')]
    [string]$ExpectedRelayHead,

    [Parameter(Mandatory = $true, ParameterSetName = 'Create')]
    [string]$ExpectedAndroidHead,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true, ParameterSetName = 'Verify')]
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [IO.Path]::GetFullPath($Path)
}

function Assert-Leaf {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }
}

function Assert-OutputPath {
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $separator = [IO.Path]::DirectorySeparatorChar
    $allowedRoot = (Get-FullPath (Join-Path $Repository 'artifacts\secure-remote-e2e-verification')).TrimEnd($separator) + $separator
    $fullPath = Get-FullPath $Path
    if (-not $fullPath.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Compatibility output must stay under $allowedRoot"
    }
}

function Get-RepositoryState {
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$ExpectedHead
    )

    $repositoryPath = Get-FullPath $Repository
    if (-not (Test-Path -LiteralPath $repositoryPath -PathType Container)) {
        throw "Repository is missing: $repositoryPath"
    }

    if ($ExpectedHead -notmatch '^[0-9a-f]{40}$') {
        throw "Expected HEAD must be a full lowercase SHA-1: $ExpectedHead"
    }

    $status = @(& git -C $repositoryPath status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect repository status: $repositoryPath"
    }
    if ($status.Count -ne 0) {
        throw "Repository must be clean: $repositoryPath`n$($status -join [Environment]::NewLine)"
    }

    $head = (& git -C $repositoryPath rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
        throw "Could not resolve repository HEAD: $repositoryPath"
    }
    if (-not [string]::Equals($head, $ExpectedHead, [StringComparison]::Ordinal)) {
        throw "Repository HEAD changed. Expected $ExpectedHead, got ${head}: $repositoryPath"
    }

    $branch = (& git -C $repositoryPath branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Could not resolve repository branch: $repositoryPath"
    }

    return [ordered]@{
        path = $repositoryPath
        branch = $branch
        head = $head
        clean = $true
    }
}

function Get-ExtendedLengthPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = Get-FullPath $Path
    if ($fullPath.StartsWith('\\?\', [StringComparison]::Ordinal)) {
        return $fullPath
    }

    return '\\?\' + $fullPath
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [IO.File]::Open((Get-ExtendedLengthPath $Path), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $hash = [Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $hash.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-FileEntry {
    param(
        [Parameter(Mandatory = $true)][string]$BundleRoot,
        [Parameter(Mandatory = $true)][string]$FilePath
    )

    $separator = [IO.Path]::DirectorySeparatorChar
    $rootPrefix = (Get-FullPath $BundleRoot).TrimEnd($separator) + $separator
    $fullPath = Get-FullPath $FilePath
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Bundle file escaped its root: $fullPath"
    }

    $file = [IO.FileInfo]::new((Get-ExtendedLengthPath $fullPath))
    if (-not $file.Exists) {
        throw "Bundle file is missing: $fullPath"
    }

    return [ordered]@{
        path = $fullPath.Substring($rootPrefix.Length).Replace([IO.Path]::DirectorySeparatorChar, '/')
        length = [Int64]$File.Length
        sha256 = Get-FileSha256 -Path $fullPath
    }
}

function Get-FileEntries {
    param([Parameter(Mandatory = $true)][string]$BundleRoot)

    $files = @(& cmd.exe /c "dir /s /b /a-d `"$BundleRoot`"" | Sort-Object)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not enumerate compatibility bundle: $BundleRoot"
    }
    if ($files.Count -eq 0) {
        throw "Compatibility bundle is empty: $BundleRoot"
    }

    return @($files | ForEach-Object { Get-FileEntry -BundleRoot $BundleRoot -FilePath $_ })
}

function Copy-DirectoryTree {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    & robocopy $Source $Destination /E /COPY:DAT /R:1 /W:1 /NJH /NJS /NDL /NFL | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "Could not copy compatibility directory: $Source"
    }
}

function Get-AndroidVersion {
    param([Parameter(Mandatory = $true)][string]$AndroidRepository)

    $buildFile = Join-Path $AndroidRepository 'app\build.gradle.kts'
    Assert-Leaf -Path $buildFile -Description 'Android build file'
    $versionName = Select-String -LiteralPath $buildFile -Pattern '^\s*versionName\s*=\s*"([^"]+)"\s*$' | Select-Object -First 1
    $versionCode = Select-String -LiteralPath $buildFile -Pattern '^\s*versionCode\s*=\s*(\d+)\s*$' | Select-Object -First 1
    if ($null -eq $versionName -or $null -eq $versionCode) {
        throw "Could not resolve Android version from $buildFile"
    }

    return "$($versionName.Matches[0].Groups[1].Value) ($($versionCode.Matches[0].Groups[1].Value))"
}

function Get-ExecutableVersion {
    param([Parameter(Mandatory = $true)][string]$Path)

    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path).ProductVersion
    if ([string]::IsNullOrWhiteSpace($version)) {
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path).FileVersion
    }
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Could not resolve executable version: $Path"
    }

    return $version
}

function Get-RelayVersion {
    param([Parameter(Mandatory = $true)][string]$Path)

    $output = @(& $Path version)
    if ($LASTEXITCODE -eq 0) {
        $versionLine = $output | Where-Object { $_ -match '^mycodex-relay\s+\S+$' } | Select-Object -First 1
        if ($null -ne $versionLine) {
            return ($versionLine -replace '^mycodex-relay\s+', '').Trim()
        }
    }

    return Get-ExecutableVersion -Path $Path
}

function Copy-Bundle {
    param(
        [Parameter(Mandatory = $true)][string]$WindowsRepository,
        [Parameter(Mandatory = $true)][string]$RelayRepository,
        [Parameter(Mandatory = $true)][string]$AndroidRepository,
        [Parameter(Mandatory = $true)][string]$BundleRoot
    )

    $windowsDebug = Join-Path $WindowsRepository 'debug'
    $windowsExe = Join-Path $windowsDebug 'MyCodex.Dev.exe'
    $embeddedRelay = Join-Path $windowsDebug 'runtime\relay\mycodex-relay.exe'
    $relayExe = Join-Path $RelayRepository 'dist\windows-x64\mycodex-relay.exe'
    $androidApk = Join-Path $AndroidRepository 'app\build\outputs\apk\debug\app-debug.apk'
    $androidTestApk = Join-Path $AndroidRepository 'app\build\outputs\apk\androidTest\debug\app-debug-androidTest.apk'
    Assert-Leaf -Path $windowsExe -Description 'Windows Dev executable'
    Assert-Leaf -Path $embeddedRelay -Description 'Embedded Relay executable'
    Assert-Leaf -Path $relayExe -Description 'Relay executable'
    Assert-Leaf -Path $androidApk -Description 'Android Debug APK'
    Assert-Leaf -Path $androidTestApk -Description 'Android test APK'

    $relayHashes = @(
        Get-FileHash -Algorithm SHA256 -LiteralPath @($relayExe, $embeddedRelay) -ErrorAction Stop
    )
    if ($relayHashes.Count -ne 2 -or @($relayHashes.Hash | Sort-Object -Unique).Count -ne 1) {
        throw 'The Windows embedded Relay does not match the Relay build artifact.'
    }

    $windowsDestination = Join-Path $BundleRoot 'windows-dev'
    $relayDestination = Join-Path $BundleRoot 'relay\windows-x64'
    $androidDestination = Join-Path $BundleRoot 'android'
    New-Item -ItemType Directory -Path $windowsDestination, $relayDestination, $androidDestination -Force | Out-Null
    Copy-DirectoryTree -Source $windowsDebug -Destination $windowsDestination
    Copy-Item -LiteralPath $relayExe -Destination (Join-Path $relayDestination 'mycodex-relay.exe') -Force
    Copy-Item -LiteralPath $androidApk -Destination (Join-Path $androidDestination 'app-debug.apk') -Force
    Copy-Item -LiteralPath $androidTestApk -Destination (Join-Path $androidDestination 'app-debug-androidTest.apk') -Force

    return [ordered]@{
        windowsVersion = Get-ExecutableVersion -Path $windowsExe
        relayVersion = Get-RelayVersion -Path $relayExe
        androidVersion = Get-AndroidVersion -AndroidRepository $AndroidRepository
        relaySha256 = $relayHashes[0].Hash.ToLowerInvariant()
    }
}

function Write-Manifest {
    param(
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][object]$Manifest
    )

    $json = $Manifest | ConvertTo-Json -Depth 8
    $manifestPath = Join-Path $OutputDirectory 'manifest.json'
    [IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    return $manifestPath
}

function Assert-ManifestFiles {
    param(
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][object]$Manifest
    )

    $bundleRoot = Join-Path $OutputDirectory 'bundle'
    if (-not (Test-Path -LiteralPath $bundleRoot -PathType Container)) {
        throw "Bundle directory is missing: $bundleRoot"
    }

    $actual = @(Get-FileEntries -BundleRoot $bundleRoot)
    $expected = @($Manifest.files)
    if ($actual.Count -ne $expected.Count) {
        throw "Manifest file count mismatch. Expected $($expected.Count), got $($actual.Count)."
    }

    $expectedByPath = @{}
    foreach ($entry in $expected) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.path) -or $expectedByPath.ContainsKey([string]$entry.path)) {
            throw 'Manifest contains an empty or duplicate bundle path.'
        }
        $expectedByPath[[string]$entry.path] = $entry
    }

    foreach ($entry in $actual) {
        if (-not $expectedByPath.ContainsKey([string]$entry.path)) {
            throw "Bundle file is absent from the manifest: $($entry.path)"
        }
        $expectedEntry = $expectedByPath[[string]$entry.path]
        if ([Int64]$expectedEntry.length -ne [Int64]$entry.length -or
            -not [string]::Equals([string]$expectedEntry.sha256, [string]$entry.sha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Manifest hash mismatch: $($entry.path)"
        }
    }
}

function Invoke-Create {
    Assert-OutputPath -Repository $WindowsRepository -Path $OutputDirectory
    $outputPath = Get-FullPath $OutputDirectory
    if (Test-Path -LiteralPath $outputPath) {
        throw "Compatibility output directory already exists: $outputPath"
    }

    $expected = [ordered]@{
        windows = $ExpectedWindowsHead
        relay = $ExpectedRelayHead
        android = $ExpectedAndroidHead
    }
    $before = [ordered]@{
        windows = Get-RepositoryState -Repository $WindowsRepository -ExpectedHead $expected.windows
        relay = Get-RepositoryState -Repository $RelayRepository -ExpectedHead $expected.relay
        android = Get-RepositoryState -Repository $AndroidRepository -ExpectedHead $expected.android
    }

    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
    $bundleRoot = Join-Path $outputPath 'bundle'
    New-Item -ItemType Directory -Path $bundleRoot -Force | Out-Null
    $versions = Copy-Bundle -WindowsRepository $before.windows.path -RelayRepository $before.relay.path -AndroidRepository $before.android.path -BundleRoot $bundleRoot

    $after = [ordered]@{
        windows = Get-RepositoryState -Repository $before.windows.path -ExpectedHead $expected.windows
        relay = Get-RepositoryState -Repository $before.relay.path -ExpectedHead $expected.relay
        android = Get-RepositoryState -Repository $before.android.path -ExpectedHead $expected.android
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        createdUtc = [DateTime]::UtcNow.ToString('o')
        script = 'New-SecureRemoteCompatibilitySet.ps1'
        expectedHeads = $expected
        before = $before
        after = $after
        versions = $versions
        files = @(Get-FileEntries -BundleRoot $bundleRoot)
    }
    $manifestPath = Write-Manifest -OutputDirectory $outputPath -Manifest $manifest
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
    Assert-ManifestFiles -OutputDirectory $outputPath -Manifest $manifest
    [IO.File]::WriteAllText((Join-Path $outputPath 'COMPLETE'), [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Output $outputPath
}

function Invoke-Verify {
    $outputPath = Get-FullPath $OutputDirectory
    $manifestPath = Join-Path $outputPath 'manifest.json'
    Assert-Leaf -Path $manifestPath -Description 'Compatibility manifest'
    Assert-Leaf -Path (Join-Path $outputPath 'COMPLETE') -Description 'Compatibility completion marker'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) {
        throw 'Unsupported compatibility manifest schema.'
    }
    Assert-ManifestFiles -OutputDirectory $outputPath -Manifest $manifest

    foreach ($name in @('windows', 'relay', 'android')) {
        $before = $manifest.before.$name
        $expected = [string]$manifest.expectedHeads.$name
        if ($null -eq $before -or [string]::IsNullOrWhiteSpace($before.path) -or [string]::IsNullOrWhiteSpace($expected)) {
            throw "Manifest repository state is incomplete: $name"
        }
        [void](Get-RepositoryState -Repository $before.path -ExpectedHead $expected)
    }
    Write-Output $outputPath
}

if ($VerifyOnly) {
    Invoke-Verify
}
else {
    Invoke-Create
}
