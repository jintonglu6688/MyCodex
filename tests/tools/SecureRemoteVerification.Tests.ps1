$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$scriptPath = Join-Path $repoRoot 'tools\remote\New-SecureRemoteCompatibilitySet.ps1'
$androidGateScriptPath = Join-Path $repoRoot 'tools\remote\Invoke-SecureRemoteAndroidGate.ps1'

function Assert-True {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-ThrowsLike {
    param([scriptblock]$Action, [string]$Pattern, [string]$Message)

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -like $Pattern) {
            return
        }
        throw "$Message Unexpected error: $($_.Exception.Message)"
    }
    throw $Message
}

function Initialize-FixtureRepository {
    param([string]$Path)

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    & git -C $Path init | Out-Null
    & git -C $Path config user.email secure-remote-test@example.invalid
    & git -C $Path config user.name SecureRemoteTest
}

function Commit-FixtureRepository {
    param([string]$Path, [string]$Message)

    & git -C $Path add -f .
    if ($LASTEXITCODE -ne 0) { throw "Could not stage fixture repository: $Path" }
    & git -C $Path commit -m $Message | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not commit fixture repository: $Path" }
}

. $androidGateScriptPath
$compatibilityScriptText = Get-Content -LiteralPath $scriptPath -Raw
Assert-True ($compatibilityScriptText -match 'function Get-RelayVersion') 'Compatibility bundle must obtain the Relay version from its version command.'
Assert-True ($compatibilityScriptText -match 'function Copy-DirectoryTree') 'Compatibility bundle must use a long-path-safe directory copy.'
Assert-True ($compatibilityScriptText -match 'function Get-ExtendedLengthPath') 'Compatibility manifest hashing must support long paths.'
$validInstrumentation = @(
    'INSTRUMENTATION_STATUS: numtests=1',
    'INSTRUMENTATION_STATUS_CODE: 1',
    'INSTRUMENTATION_STATUS: mycodex_secure_remote_gate=IDENTITY_BINDING_CHANNEL_VERIFIED',
    'INSTRUMENTATION_STATUS_CODE: 0',
    'OK (1 test)'
)
Assert-SecureRemoteInstrumentationResult -ExitCode 0 -OutputLines $validInstrumentation
Assert-ThrowsLike {
    Assert-SecureRemoteInstrumentationResult -ExitCode 0 -OutputLines @('INSTRUMENTATION_STATUS: numtests=0', 'OK (0 tests)')
} '*Targeted secure relay instrumentation failed.*' 'Zero instrumentation tests passed the gate.'
Assert-ThrowsLike {
    Assert-SecureRemoteInstrumentationResult -ExitCode 0 -OutputLines @()
} '*Targeted secure relay instrumentation failed.*' 'Empty instrumentation output passed the gate.'
Assert-ThrowsLike {
    Assert-SecureRemoteInstrumentationResult -ExitCode 0 -OutputLines @('')
} '*Targeted secure relay instrumentation failed.*' 'Blank instrumentation output passed the gate.'
Assert-ThrowsLike {
    Assert-SecureRemoteInstrumentationResult -ExitCode 0 -OutputLines @(
        'INSTRUMENTATION_STATUS: numtests=1',
        'INSTRUMENTATION_STATUS_CODE: -3',
        'OK (1 test)')
} '*Targeted secure relay instrumentation failed.*' 'Ignored instrumentation test passed the gate.'
Assert-ThrowsLike {
    Assert-SecureRemoteInstrumentationResult -ExitCode 0 -OutputLines @(
        'INSTRUMENTATION_STATUS: numtests=1',
        'INSTRUMENTATION_STATUS_CODE: -4',
        'OK (1 test)')
} '*Targeted secure relay instrumentation failed.*' 'Assumption-failed instrumentation test passed the gate.'

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('MyCodex-SecureRemoteVerification-' + [Guid]::NewGuid().ToString('N'))
try {
    $windowsRepository = Join-Path $testRoot 'windows'
    $relayRepository = Join-Path $testRoot 'relay'
    $androidRepository = Join-Path $testRoot 'android'
    Initialize-FixtureRepository $windowsRepository
    Initialize-FixtureRepository $relayRepository
    Initialize-FixtureRepository $androidRepository
    Set-Content -LiteralPath (Join-Path $windowsRepository '.gitignore') -Value 'artifacts/' -Encoding ascii

    $systemExecutable = Join-Path ([Environment]::SystemDirectory) 'cmd.exe'
    Assert-True (Test-Path -LiteralPath $systemExecutable -PathType Leaf) 'Windows cmd.exe fixture is missing.'
    $windowsDebug = Join-Path $windowsRepository 'debug'
    $windowsRelay = Join-Path $windowsDebug 'runtime\relay'
    $relayDist = Join-Path $relayRepository 'dist\windows-x64'
    New-Item -ItemType Directory -Path $windowsRelay, $relayDist -Force | Out-Null
    Copy-Item -LiteralPath $systemExecutable -Destination (Join-Path $windowsDebug 'MyCodex.Dev.exe')
    Copy-Item -LiteralPath $systemExecutable -Destination (Join-Path $windowsRelay 'mycodex-relay.exe')
    Copy-Item -LiteralPath $systemExecutable -Destination (Join-Path $relayDist 'mycodex-relay.exe')

    $androidBuild = Join-Path $androidRepository 'app\build.gradle.kts'
    $androidApkDirectory = Join-Path $androidRepository 'app\build\outputs\apk\debug'
    $androidTestDirectory = Join-Path $androidRepository 'app\build\outputs\apk\androidTest\debug'
    New-Item -ItemType Directory -Path (Split-Path $androidBuild -Parent), $androidApkDirectory, $androidTestDirectory -Force | Out-Null
    @(
        'android {',
        '  defaultConfig {',
        '    versionCode = 1',
        '    versionName = "1.0"',
        '  }',
        '}'
    ) | Set-Content -LiteralPath $androidBuild -Encoding utf8
    Set-Content -LiteralPath (Join-Path $androidApkDirectory 'app-debug.apk') -Value 'fixture-apk' -Encoding ascii
    Set-Content -LiteralPath (Join-Path $androidTestDirectory 'app-debug-androidTest.apk') -Value 'fixture-test-apk' -Encoding ascii
    Commit-FixtureRepository $windowsRepository 'fixture windows'
    Commit-FixtureRepository $relayRepository 'fixture relay'
    Commit-FixtureRepository $androidRepository 'fixture android'

    $windowsHead = (& git -C $windowsRepository rev-parse HEAD).Trim()
    $relayHead = (& git -C $relayRepository rev-parse HEAD).Trim()
    $androidHead = (& git -C $androidRepository rev-parse HEAD).Trim()
    $outputDirectory = Join-Path $windowsRepository 'artifacts\secure-remote-e2e-verification\happy'
    & $scriptPath `
        -WindowsRepository $windowsRepository `
        -RelayRepository $relayRepository `
        -AndroidRepository $androidRepository `
        -ExpectedWindowsHead $windowsHead `
        -ExpectedRelayHead $relayHead `
        -ExpectedAndroidHead $androidHead `
        -OutputDirectory $outputDirectory | Out-Null
    Assert-True (Test-Path -LiteralPath (Join-Path $outputDirectory 'COMPLETE') -PathType Leaf) 'Happy-path compatibility set did not write COMPLETE.'
    & $scriptPath -VerifyOnly -OutputDirectory $outputDirectory | Out-Null

    Add-Content -LiteralPath (Join-Path $outputDirectory 'bundle\android\app-debug.apk') -Value 'tampered'
    Assert-ThrowsLike {
        & $scriptPath -VerifyOnly -OutputDirectory $outputDirectory
    } '*Manifest hash mismatch*' 'Tampered compatibility bundle passed verification.'

    Set-Content -LiteralPath (Join-Path $relayRepository 'clean-head-change.txt') -Value 'new head' -Encoding ascii
    Commit-FixtureRepository $relayRepository 'fixture relay changed'
    $changedOutput = Join-Path $windowsRepository 'artifacts\secure-remote-e2e-verification\changed-head'
    Assert-ThrowsLike {
        & $scriptPath `
            -WindowsRepository $windowsRepository `
            -RelayRepository $relayRepository `
            -AndroidRepository $androidRepository `
            -ExpectedWindowsHead $windowsHead `
            -ExpectedRelayHead $relayHead `
            -ExpectedAndroidHead $androidHead `
            -OutputDirectory $changedOutput
    } '*Repository HEAD changed*' 'A clean repository changed after build was accepted.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $changedOutput 'COMPLETE') -PathType Leaf)) 'Changed-head failure wrote COMPLETE.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Host 'SecureRemoteVerification.Tests.ps1 passed.'
