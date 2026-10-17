param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [string]$SourceDir,
    [string]$OutputDir,
    [string]$BundledCodexVersion,
    [switch]$SkipBuild,
    [switch]$UseInstalledBundledCodexRuntime,
    [switch]$AllowDevelopmentAuthenticationHost,
    [string]$AuthenticationPackageRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
. (Join-Path $repoRoot 'tools\authentication-center\Test-AuthenticationHostPackage.ps1')
if ($AllowDevelopmentAuthenticationHost -and $Configuration -ne 'Debug') { throw 'Development authentication packages require an explicit Debug installer build.' }
if ([string]::IsNullOrWhiteSpace($AuthenticationPackageRoot)) { $AuthenticationPackageRoot = Join-Path $repoRoot 'runtime\auth-core' }
$authenticationPackageRoot = [IO.Path]::GetFullPath($AuthenticationPackageRoot)
$authenticationPackage = Test-AuthenticationHostPackage -PackageRoot $authenticationPackageRoot -AllowDevelopment:$AllowDevelopmentAuthenticationHost
$versionPropsPath = Join-Path $repoRoot "Version.props"
$issPath = Join-Path $PSScriptRoot "MyCodex.iss"
$codexTargetTriple = if ($env:PROCESSOR_ARCHITEW6432 -eq "ARM64" -or $env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "aarch64-pc-windows-msvc" } else { "x86_64-pc-windows-msvc" }
function Get-AppVersion {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Version.props was not found: $Path"
    }

    $content = Get-Content -LiteralPath $Path -Raw
    $match = [regex]::Match($content, "<Version>([^<]+)</Version>")
    if (-not $match.Success) {
        throw "Unable to read <Version> from $Path"
    }

    return $match.Groups[1].Value.Trim()
}

function ConvertTo-CodexRuntimeVersion {
    param([string]$VersionName)

    if ([string]::IsNullOrWhiteSpace($VersionName)) {
        return $null
    }

    $normalized = $VersionName.Trim()
    $suffixIndex = $normalized.IndexOf("-")
    if ($suffixIndex -gt 0) {
        $normalized = $normalized.Substring(0, $suffixIndex)
    }

    $parsed = $null
    if ([System.Version]::TryParse($normalized, [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Compare-CodexRuntimeVersion {
    param(
        [string]$LeftName,
        [System.Version]$LeftVersion,
        [string]$RightName,
        [System.Version]$RightVersion
    )

    if ($null -ne $LeftVersion -and $null -ne $RightVersion) {
        $parsedComparison = $LeftVersion.CompareTo($RightVersion)
        if ($parsedComparison -ne 0) {
            return $parsedComparison
        }
    } elseif ($null -ne $LeftVersion) {
        return 1
    } elseif ($null -ne $RightVersion) {
        return -1
    }

    return [string]::Compare($LeftName, $RightName, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-IsccPath {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "ISCC.exe was not found. Please verify that Inno Setup 6 is installed."
}

function Get-MaximumInstallRootLength {
    param([string]$PackageSourceDir)

    $root = [IO.Path]::GetFullPath($PackageSourceDir).TrimEnd('\')
    $limit = 259
    # Inno Setup 6 creates an is-XXXXXXXXXX.tmp (17 characters) beside each file.
    foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse -Force | Where-Object { $_.Extension -ine '.pdb' }) {
        $relativeLength = $file.FullName.Length - $root.Length
        $temporaryLength = $file.DirectoryName.Length - $root.Length + 1 + 17
        $limit = [Math]::Min($limit, 259 - [Math]::Max($relativeLength, $temporaryLength))
    }
    if ($limit -lt 60) { throw 'Package paths are too deep for the default per-user installation. Shorten the packaged directory layout.' }
    return $limit
}

function Test-IsPathUnderRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $rootWithSeparator = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar

    return $resolvedPath -eq $resolvedRoot -or
        ($resolvedPath -ne $resolvedRoot -and $resolvedPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase))
}

function Remove-DirectorySafely {
    param(
        [string]$Path,
        [string]$AllowedRoot
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $resolvedRoot = (Resolve-Path -LiteralPath $AllowedRoot).Path
    if (-not (Test-IsPathUnderRoot -Path $resolvedPath -Root $resolvedRoot)) {
        throw "Refusing to delete outside allowed root: $resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

function Test-BundledCodexRuntimeExists {
    param(
        [string]$Version,
        [string]$TargetTriple,
        [string]$Root = $repoRoot
    )

    $runtimeRoot = Join-Path $Root ("runtime\codex\" + $Version + "\" + $TargetTriple + "\vendor\" + $TargetTriple)
    $candidateBin = Join-Path $runtimeRoot "bin\codex.exe"
    $candidateNested = Join-Path $runtimeRoot "codex\codex.exe"
    return (Test-Path -LiteralPath $candidateBin) -or (Test-Path -LiteralPath $candidateNested)
}

function Get-LatestInstalledBundledCodexVersion {
    param(
        [string]$TargetTriple,
        [string]$Root = $repoRoot
    )

    $runtimeRoot = Join-Path $Root "runtime\codex"
    if (-not (Test-Path -LiteralPath $runtimeRoot)) {
        return $null
    }

    $bestVersionName = $null
    $bestVersion = $null
    foreach ($runtimeDirectory in Get-ChildItem -LiteralPath $runtimeRoot -Directory) {
        $versionName = $runtimeDirectory.Name
        if ([string]::IsNullOrWhiteSpace($versionName) -or
            $versionName.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
            continue
        }

        if (-not (Test-BundledCodexRuntimeExists -Version $versionName -TargetTriple $TargetTriple -Root $Root)) {
            continue
        }

        $parsedVersion = ConvertTo-CodexRuntimeVersion -VersionName $versionName
        if ([string]::IsNullOrWhiteSpace($bestVersionName) -or
            (Compare-CodexRuntimeVersion `
                -LeftName $versionName `
                -LeftVersion $parsedVersion `
                -RightName $bestVersionName `
                -RightVersion $bestVersion) -gt 0) {
            $bestVersionName = $versionName
            $bestVersion = $parsedVersion
        }
    }

    return $bestVersionName
}

function Resolve-BundledCodexPackageVersion {
    param(
        [string]$RequestedVersion,
        [string]$TargetTriple,
        [switch]$UseInstalledRuntime
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        Write-Host "Using requested bundled Codex runtime $($RequestedVersion.Trim()) $TargetTriple for installer package."
        return $RequestedVersion.Trim()
    }

    if ($UseInstalledRuntime.IsPresent) {
        $installedVersion = Get-LatestInstalledBundledCodexVersion -TargetTriple $TargetTriple
        if (-not [string]::IsNullOrWhiteSpace($installedVersion)) {
            Write-Host "Using installed bundled Codex runtime $installedVersion $TargetTriple for installer package."
            return $installedVersion
        }

        Write-Host "No installed bundled Codex runtime found for $TargetTriple."
    }

    Write-Host "Preparing official latest bundled Codex runtime for $TargetTriple."
    return "latest"
}

function Remove-NonPinnedBundledCodexRuntimes {
    param(
        [string]$PackageSourceDir,
        [string]$RequiredVersion
    )

    if ([string]::IsNullOrWhiteSpace($PackageSourceDir) -or [string]::IsNullOrWhiteSpace($RequiredVersion)) {
        return
    }

    $packageRuntimeRoot = Join-Path $PackageSourceDir "runtime\codex"
    if (-not (Test-Path -LiteralPath $packageRuntimeRoot)) {
        return
    }

    foreach ($runtimeDirectory in Get-ChildItem -LiteralPath $packageRuntimeRoot -Directory) {
        if ([string]::Equals($runtimeDirectory.Name, $RequiredVersion, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        Write-Host "Removing non-pinned bundled Codex runtime from package source: $($runtimeDirectory.FullName)"
        Remove-DirectorySafely -Path $runtimeDirectory.FullName -AllowedRoot $packageRuntimeRoot
    }
}

function Remove-RetiredBundledNodeRuntime {
    param([string]$PackageSourceDir)

    if ([string]::IsNullOrWhiteSpace($PackageSourceDir)) {
        return
    }

    $retiredRuntimeDirectory = Join-Path $PackageSourceDir "runtime\node"
    if (Test-Path -LiteralPath $retiredRuntimeDirectory) {
        Write-Host "Removing retired bundled Node runtime from package source: $retiredRuntimeDirectory"
        Remove-DirectorySafely -Path $retiredRuntimeDirectory -AllowedRoot $PackageSourceDir
    }
}

function Assert-ComputerUseBundle {
    param(
        [string]$PackageSourceDir
    )

    # Verify the repository bundle first, then require the same pinned bundle in the package.
    & (Join-Path $repoRoot 'tools\runtime\prepare-computer-use-runtime.ps1')
    $pin = Get-Content -LiteralPath (Join-Path $repoRoot 'tools\runtime\computer-use-bundle.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $bundlesRoot = Join-Path $PackageSourceDir 'runtime\cua'
    $bundleRoot = Join-Path $bundlesRoot $pin.bundleId
    $manifestPath = Join-Path $bundleRoot 'bundle.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $pin.manifestSha256) {
        throw "Computer Use bundle $($pin.bundleId) is missing from the package source or does not match the pin. Rebuild after preparing runtime\cua."
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $count = @(Get-ChildItem -LiteralPath $bundleRoot -File -Recurse | Where-Object { $_.FullName -ne $manifestPath }).Count
    if ($count -ne $manifest.files.Count) {
        throw "Computer Use bundle $($pin.bundleId) in the package source is incomplete."
    }
    foreach ($stale in Get-ChildItem -LiteralPath $bundlesRoot -Directory | Where-Object { $_.Name -ne $pin.bundleId }) {
        Remove-DirectorySafely -Path $stale.FullName -AllowedRoot $bundlesRoot
    }
}

function Copy-BundledRelay {
    param(
        [string]$PackageSourceDir
    )

    if ([string]::IsNullOrWhiteSpace($PackageSourceDir)) {
        throw "Package source directory is required before copying bundled Relay."
    }

    Remove-LegacyRelay -PackageSourceDir $PackageSourceDir
    $relayDestination = Join-Path $PackageSourceDir "runtime\relay\mycodex-relay.exe"
    & (Join-Path $repoRoot "tools\runtime\prepare-relay-runtime.ps1") -DestinationPath $relayDestination
}

function Remove-LegacyRelay {
    param(
        [string]$PackageSourceDir
    )

    if ([string]::IsNullOrWhiteSpace($PackageSourceDir)) {
        return
    }

    foreach ($relayDirectory in @((Join-Path $PackageSourceDir "relay"))) {
        if (Test-Path -LiteralPath $relayDirectory) {
            Write-Host "Removing legacy Relay from package source: $relayDirectory"
            Remove-DirectorySafely -Path $relayDirectory -AllowedRoot $PackageSourceDir
        }
    }
}

$version = Get-AppVersion -Path $versionPropsPath
$bundledCodexVersion = Resolve-BundledCodexPackageVersion `
    -RequestedVersion $BundledCodexVersion `
    -TargetTriple $codexTargetTriple `
    -UseInstalledRuntime:$UseInstalledBundledCodexRuntime

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = Join-Path $repoRoot ("artifacts\package\MyCodex_v" + $version)
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts\installer"
}

$sourceDirFull = [IO.Path]::GetFullPath($SourceDir)
$outputDirFull = [IO.Path]::GetFullPath($OutputDir)
$artifactRoot = Join-Path $repoRoot "artifacts"

if (-not $SkipBuild) {
    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    Remove-DirectorySafely -Path $sourceDirFull -AllowedRoot $artifactRoot
    New-Item -ItemType Directory -Path $sourceDirFull -Force | Out-Null

    $isLatestBundledCodexRequest = [string]::Equals($bundledCodexVersion, "latest", [System.StringComparison]::OrdinalIgnoreCase)
    if (-not $isLatestBundledCodexRequest -and
        (Test-BundledCodexRuntimeExists -Version $bundledCodexVersion -TargetTriple $codexTargetTriple)) {
        Write-Host "Bundled Codex runtime $bundledCodexVersion $codexTargetTriple already exists."
    } else {
        Write-Host "Preparing bundled Codex runtime $bundledCodexVersion $codexTargetTriple"
        & (Join-Path $repoRoot "tools\runtime\prepare-bundled-codex-runtime.ps1") `
            -Version $bundledCodexVersion `
            -TargetTriple $codexTargetTriple `
            -DestinationRoot $repoRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Bundled Codex runtime preparation failed."
        }
    }

    if ($isLatestBundledCodexRequest) {
        $resolvedBundledCodexVersion = Get-LatestInstalledBundledCodexVersion -TargetTriple $codexTargetTriple
        if ([string]::IsNullOrWhiteSpace($resolvedBundledCodexVersion)) {
            throw "Bundled Codex runtime could not be resolved after preparing official latest runtime."
        }

        $bundledCodexVersion = $resolvedBundledCodexVersion
        Write-Host "Resolved official latest bundled Codex runtime to $bundledCodexVersion $codexTargetTriple."
    }

    if (-not (Test-BundledCodexRuntimeExists -Version $bundledCodexVersion -TargetTriple $codexTargetTriple)) {
        throw "Bundled Codex runtime was not found under runtime\codex after preparation."
    }

    Write-Host "Building MyCodex $version to $sourceDirFull"
    dotnet build (Join-Path $repoRoot "src\MyCodex.WinForms\MyCodex.WinForms.csproj") `
        -c $Configuration `
        -p:Platform=x64 `
        -o $sourceDirFull

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed."
    }

}

$mainExecutablePath = Join-Path $sourceDirFull "MyCodex.exe"
if (-not (Test-Path -LiteralPath $mainExecutablePath)) {
    throw "MyCodex.exe was not found in package source directory: $sourceDirFull"
}

Copy-BundledRelay -PackageSourceDir $sourceDirFull
& (Join-Path $repoRoot 'tools\runtime\prepare-git-runtime.ps1') -DestinationRoot (Join-Path $sourceDirFull 'runtime\git')
$authenticationDestination = Join-Path $sourceDirFull 'runtime\auth-core'
[IO.Directory]::CreateDirectory($authenticationDestination) | Out-Null
foreach ($name in @('mycodex-auth-host.exe', 'mycodex-auth-host.manifest.json', 'LICENSE')) {
    $source = Join-Path $authenticationPackageRoot $name
    $destination = Join-Path $authenticationDestination $name
    if ([IO.Path]::GetFullPath($source) -ine [IO.Path]::GetFullPath($destination)) { Copy-Item -LiteralPath $source -Destination $destination -Force }
}
$authenticationPackage = Test-AuthenticationHostPackage -PackageRoot $authenticationDestination -AllowDevelopment:$AllowDevelopmentAuthenticationHost
Remove-DirectorySafely -Path (Join-Path $sourceDirFull 'runtime\auth-center') -AllowedRoot $sourceDirFull
Remove-RetiredBundledNodeRuntime -PackageSourceDir $sourceDirFull
Remove-DirectorySafely -Path (Join-Path $sourceDirFull 'runtime\computer-use') -AllowedRoot $sourceDirFull
Assert-ComputerUseBundle -PackageSourceDir $sourceDirFull

if ([string]::Equals($bundledCodexVersion, "latest", [System.StringComparison]::OrdinalIgnoreCase)) {
    $resolvedBundledCodexVersion = Get-LatestInstalledBundledCodexVersion `
        -TargetTriple $codexTargetTriple `
        -Root $sourceDirFull
    if ([string]::IsNullOrWhiteSpace($resolvedBundledCodexVersion)) {
        throw "Bundled Codex runtime could not be resolved from package source directory."
    }

    $bundledCodexVersion = $resolvedBundledCodexVersion
    Write-Host "Resolved package source bundled Codex runtime to $bundledCodexVersion $codexTargetTriple."
}

Remove-NonPinnedBundledCodexRuntimes `
    -PackageSourceDir $sourceDirFull `
    -RequiredVersion $bundledCodexVersion

if (-not (Test-BundledCodexRuntimeExists -Version $bundledCodexVersion -TargetTriple $codexTargetTriple -Root $sourceDirFull)) {
    throw "Bundled Codex runtime $bundledCodexVersion $codexTargetTriple was not found in package source directory."
}

New-Item -ItemType Directory -Path $outputDirFull -Force | Out-Null
$isccPath = Get-IsccPath
$maximumInstallRootLength = Get-MaximumInstallRootLength -PackageSourceDir $sourceDirFull

Write-Host "Compiling installer with Inno Setup: $isccPath"
$developmentInstallerArguments = @()
if ($AllowDevelopmentAuthenticationHost) { $developmentInstallerArguments += '/DDevelopmentAuthenticationHost' }
& $isccPath `
    "/DMyCodexVersion=$version" `
    "/DSourceDir=$sourceDirFull" `
    "/DOutputDir=$outputDirFull" `
    "/DMaximumInstallRootLength=$maximumInstallRootLength" `
    @developmentInstallerArguments `
    $issPath

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed."
}

Write-Host ""
Write-Host "Installer created:"
Write-Host (Join-Path $outputDirFull ("MyCodex_v" + $version + "_Setup.exe"))
$installerPath = Join-Path $outputDirFull ("MyCodex_v" + $version + "_Setup.exe")
$receipt = [ordered]@{ schemaVersion = 1; development = [bool]$AllowDevelopmentAuthenticationHost; installerSha256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant(); authenticationHost = $authenticationPackage }
[IO.File]::WriteAllText(($installerPath + '.auth.json'), (($receipt | ConvertTo-Json -Depth 5) + "`n"), [Text.UTF8Encoding]::new($false))
if ($AllowDevelopmentAuthenticationHost) { Write-Warning 'This installer is for isolated development acceptance only and cannot be published by the release entry point.' }
