param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Release", "Dev")]
    [string]$Profile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "src\MyCodex.WinForms\MyCodex.WinForms.csproj"
. (Join-Path $PSScriptRoot "ScriptHelpers.ps1")

function Assert-PathUnderRoot {
    param(
        [string]$Path,
        [string]$Root
    )

    if (-not (Test-IsPathUnderRoot -Path $Path -Root $Root)) {
        throw "Refusing to modify a path outside the allowed root: $Path"
    }
}

function Find-BundledCodexRuntime {
    param([string]$TargetTriple)

    $runtimeRoot = Join-Path $repoRoot "runtime\codex"
    if (-not (Test-Path -LiteralPath $runtimeRoot -PathType Container)) {
        return $null
    }

    foreach ($versionDirectory in Get-ChildItem -LiteralPath $runtimeRoot -Directory) {
        $candidatePaths = @(
            (Join-Path $versionDirectory.FullName "$TargetTriple\vendor\$TargetTriple\bin\codex.exe"),
            (Join-Path $versionDirectory.FullName "$TargetTriple\vendor\$TargetTriple\codex\codex.exe")
        )
        foreach ($candidatePath in $candidatePaths) {
            if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
                return [PSCustomObject]@{
                    Version = $versionDirectory.Name
                    ExecutablePath = $candidatePath
                }
            }
        }
    }

    return $null
}

function Remove-LegacyOutputFiles {
    param(
        [string]$OutputDirectory,
        [bool]$RemovePdbFiles
    )

    if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) {
        return
    }

    Assert-PathUnderRoot -Path $OutputDirectory -Root $repoRoot

    if ($RemovePdbFiles) {
        Get-ChildItem -LiteralPath $OutputDirectory -Filter "*.pdb" -File |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }

    $legacyBoyCtrlDirectory = Join-Path $OutputDirectory "tts\BoyCtrl"
    Assert-PathUnderRoot -Path $legacyBoyCtrlDirectory -Root $OutputDirectory
    if (Test-Path -LiteralPath $legacyBoyCtrlDirectory) {
        Remove-Item -LiteralPath $legacyBoyCtrlDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }

    $zdsrApiDirectory = Join-Path $OutputDirectory "tts\ZDSRAPI"
    foreach ($fileName in @("ZDSRAPI.ini", "ZDSRAPI.txt")) {
        $legacyFilePath = Join-Path $zdsrApiDirectory $fileName
        Assert-PathUnderRoot -Path $legacyFilePath -Root $OutputDirectory
        if (Test-Path -LiteralPath $legacyFilePath -PathType Leaf) {
            Remove-Item -LiteralPath $legacyFilePath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Remove-LegacyRelay {
    param([string]$OutputDirectory)

    foreach ($relativeDirectory in @("relay")) {
        $relayDirectory = Join-Path $OutputDirectory $relativeDirectory
        Assert-PathUnderRoot -Path $relayDirectory -Root $OutputDirectory
        $relayExecutable = Join-Path $relayDirectory "mycodex-relay.exe"
        if (Test-Path -LiteralPath $relayExecutable -PathType Leaf) {
            Remove-Item -LiteralPath $relayExecutable -Force -ErrorAction SilentlyContinue
        }

        if ((Test-Path -LiteralPath $relayDirectory -PathType Container) -and
            -not (Get-ChildItem -LiteralPath $relayDirectory -Force | Select-Object -First 1)) {
            Remove-Item -LiteralPath $relayDirectory -Force -ErrorAction SilentlyContinue
        }
    }
}

function Copy-BundledRelay {
    param([string]$OutputDirectory)

    $relayDestinationDirectory = Join-Path $OutputDirectory "runtime\relay"
    $relayDestination = Join-Path $relayDestinationDirectory "mycodex-relay.exe"

    Remove-LegacyRelay -OutputDirectory $OutputDirectory
    Assert-PathUnderRoot -Path $relayDestinationDirectory -Root $OutputDirectory
    & (Join-Path $repoRoot "tools\runtime\prepare-relay-runtime.ps1") -DestinationPath $relayDestination
}

$isArm64 = [string]::Equals($env:PROCESSOR_ARCHITECTURE, "ARM64", [StringComparison]::OrdinalIgnoreCase) -or
           [string]::Equals($env:PROCESSOR_ARCHITEW6432, "ARM64", [StringComparison]::OrdinalIgnoreCase)
$targetTriple = if ($isArm64) { "aarch64-pc-windows-msvc" } else { "x86_64-pc-windows-msvc" }

# ponytail: fixed profiles keep cleanup targets limited to known repository outputs.
if ([string]::Equals($Profile, "Dev", [StringComparison]::OrdinalIgnoreCase)) {
    $configuration = "Debug"
    $outputDirectory = Join-Path $repoRoot "debug"
    $outputDescription = Join-Path $outputDirectory "MyCodex.Dev.exe"
    $removePdbFiles = $false
} else {
    $configuration = "Release"
    $outputDirectory = Join-Path $repoRoot "Release"
    $outputDescription = $outputDirectory
    $removePdbFiles = $true
}
Assert-PathUnderRoot -Path $outputDirectory -Root $repoRoot

# Dev reuses a verified local backend; Release prepares the pinned Windows release.
& (Join-Path $repoRoot "tools\authentication-center\Prepare-AuthenticationHost.ps1") -Configuration $configuration
. (Join-Path $repoRoot "tools\authentication-center\Test-AuthenticationHostPackage.ps1")
Test-AuthenticationHostPackage -PackageRoot (Join-Path $repoRoot 'runtime\auth-core') -AllowDevelopment | Out-Null

$bundledRuntime = Find-BundledCodexRuntime -TargetTriple $targetTriple
if ($null -ne $bundledRuntime) {
    Write-Host "[1/4] Bundled Codex runtime $($bundledRuntime.Version) already exists."
} else {
    Write-Host "[1/4] Bundled Codex runtime is missing."
    Write-Host "Downloading latest from official Codex package source..."
    & (Join-Path $repoRoot "tools\runtime\prepare-bundled-codex-runtime.ps1") `
        -TargetTriple $targetTriple `
        -DestinationRoot $repoRoot
}

& (Join-Path $repoRoot 'tools\runtime\prepare-git-runtime.ps1')

# The project copies whatever is under runtime\cua, so a missing bundle would be omitted silently.
try {
    & (Join-Path $repoRoot 'tools\runtime\prepare-computer-use-runtime.ps1')
} catch {
    if (-not [string]::Equals($Profile, "Dev", [StringComparison]::OrdinalIgnoreCase)) { throw }
    Write-Warning "This Dev build omits Computer Use: $($_.Exception.Message)"
}

try {
    & (Join-Path $repoRoot 'tools\runtime\prepare-browser-use-runtime.ps1')
    & (Join-Path $repoRoot 'tools\runtime\prepare-browser-extension-host.ps1')
} catch {
    if (-not [string]::Equals($Profile, "Dev", [StringComparison]::OrdinalIgnoreCase)) { throw }
    Write-Warning "This Dev build omits browser automation: $($_.Exception.Message)"
}

Write-Host "[2/4] Building $Profile..."
$buildArguments = @(
    "build",
    $projectPath,
    "-c",
    $configuration,
    "-p:Platform=x64"
)
if ([string]::Equals($Profile, "Dev", [StringComparison]::OrdinalIgnoreCase)) {
    $buildArguments += "-p:MyCodexChannel=Dev"
    $buildArguments += "-v:minimal"
}

& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "$Profile build failed with exit code $LASTEXITCODE."
}

Write-Host "[3/4] Cleaning obsolete files..."
Remove-LegacyOutputFiles -OutputDirectory $outputDirectory -RemovePdbFiles $removePdbFiles

Copy-BundledRelay -OutputDirectory $outputDirectory

Write-Host "[4/4] Done."
Write-Host "Output: `"$outputDescription`""
