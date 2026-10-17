[CmdletBinding()]
param(
    [string]$CompatibilityRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-SecureRemoteInstrumentationResult {
    param(
        [Parameter(Mandatory = $true)][int]$ExitCode,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][AllowEmptyString()][string[]]$OutputLines
    )

    $expectedTests = 1
    $completionMarker = 'mycodex_secure_remote_gate=IDENTITY_BINDING_CHANNEL_VERIFIED'
    $outputText = $OutputLines -join "`n"
    $numTests = @(
        [regex]::Matches($outputText, '(?m)^INSTRUMENTATION_STATUS: numtests=(\d+)\s*$') |
            ForEach-Object { [int]$_.Groups[1].Value } |
            Sort-Object -Unique
    )
    $okMatches = [regex]::Matches($outputText, '(?m)^OK \((\d+) tests?\)\s*$')
    $completionMatches = [regex]::Matches(
        $outputText,
        ('(?m)^INSTRUMENTATION_STATUS: ' + [regex]::Escape($completionMarker) + '\s*$'))
    $negativeStatusMatches = [regex]::Matches(
        $outputText,
        '(?m)^INSTRUMENTATION_STATUS_CODE: -\d+\s*$')

    if ($ExitCode -ne 0 -or
        $outputText -match 'FAILURES!!!|INSTRUMENTATION_FAILED' -or
        $negativeStatusMatches.Count -ne 0 -or
        $numTests.Count -ne 1 -or
        $numTests[0] -ne $expectedTests -or
        $okMatches.Count -ne 1 -or
        [int]$okMatches[0].Groups[1].Value -ne $expectedTests -or
        $completionMatches.Count -ne 1) {
        $OutputLines | Write-Host
        throw 'Targeted secure relay instrumentation failed.'
    }
}

if ($MyInvocation.InvocationName -eq '.') {
    return
}

if ([string]::IsNullOrWhiteSpace($CompatibilityRoot)) {
    throw 'CompatibilityRoot is required.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $repoRoot 'tools\remote\New-SecureRemoteCompatibilitySet.ps1') `
    -VerifyOnly `
    -OutputDirectory $CompatibilityRoot | Out-Null

$devices = @(
    & adb devices |
        Select-String '^\S+\s+device$' |
        ForEach-Object { ($_ -split '\s+')[0] }
)
if ($devices.Count -ne 1) {
    throw "Expected exactly one authorized Android device, found $($devices.Count)."
}

$serial = $devices[0]
$apiLevelText = (& adb -s $serial shell getprop ro.build.version.sdk).Trim()
if ($LASTEXITCODE -ne 0 -or $apiLevelText -notmatch '^\d+$' -or [int]$apiLevelText -lt 31) {
    throw 'The Android device must be authorized and API 31 or newer.'
}

$appApk = Join-Path $CompatibilityRoot 'bundle\android\app-debug.apk'
$testApk = Join-Path $CompatibilityRoot 'bundle\android\app-debug-androidTest.apk'
foreach ($apk in @($appApk, $testApk)) {
    if (-not (Test-Path -LiteralPath $apk -PathType Leaf)) {
        throw "Compatibility-set APK is missing: $apk"
    }
}

& adb -s $serial install -r -t $appApk | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'Android Debug install failed.' }
& adb -s $serial install -r -t $testApk | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'Android test APK install failed.' }

$instrumentationOutput = @(
    & adb -s $serial shell am instrument -w -r `
        -e class com.mycodex.mobile.relay.SecureRelayChannelInstrumentedTest `
        com.mycodex.mobile.test/androidx.test.runner.AndroidJUnitRunner 2>&1 |
        ForEach-Object { $_.ToString() }
)
$instrumentationExitCode = $LASTEXITCODE
Assert-SecureRemoteInstrumentationResult `
    -ExitCode $instrumentationExitCode `
    -OutputLines $instrumentationOutput

& adb -s $serial shell pm path com.mycodex.mobile | Write-Host
if ($LASTEXITCODE -ne 0) { throw 'Android package verification failed.' }
