$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$work = Join-Path $repo ('artifacts\resource-diagnostics-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
$report = Join-Path $work 'report.txt'
$install = Join-Path $work 'install'
. (Join-Path $repo 'tools\installer\Test-MyCodexResourceUsers.ps1') -InstallRoot $install -FunctionsOnly
function Assert($value, $message) { if (-not $value) { throw $message } }
function Guard([string[]]$Locked, [switch]$IncludeManagedProcesses, [switch]$AllowLegacyNodeCleanup, [string]$UninstallerPath) {
    try {
        Assert-NoMyCodexResourceUsers -Root $install -LockedImages $Locked -DiagnosticPath $report -IncludeManagedProcesses:$IncludeManagedProcesses -AllowLegacyNodeCleanup:$AllowLegacyNodeCleanup -UninstallerPath $UninstallerPath
        return 'free'
    } catch { return $_.Exception.Message }
}
function Invoke-GuardScript {
    # Run the production entry point; its stderr code is expected and must not stop this test.
    $ErrorActionPreference = 'Continue'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tools\installer\Test-MyCodexResourceUsers.ps1') -InstallRoot $install -DiagnosticPath $report 2>&1 | Out-Null
    return $LASTEXITCODE
}

try {
    # Locked images decide the result; process identities are never consulted.
    Assert ((Guard @('MyCodex.exe', 'runtime\codex\0.161.0\codex.exe')) -eq 'mycodex_resource_still_in_use') 'Locked client was accepted'
    $text = Get-Content -LiteralPath $report -Raw
    Assert ($text.Contains('in_use path=MyCodex.exe') -and $text.Contains('in_use path=runtime\codex\0.161.0\codex.exe')) 'Missing blockers'
    Assert (-not $text.Contains($work)) 'Report contains absolute paths'
    Assert ((Guard @()) -eq 'free') 'Idle installation was blocked'
    $uninstaller = Join-Path $install 'unins000.exe'
    Assert ((Guard @('unins000.exe') -UninstallerPath $uninstaller -IncludeManagedProcesses) -eq 'free') 'Uninstaller blocked itself'
    Assert ((Guard @('unins000.exe')) -eq 'mycodex_resource_still_in_use') 'Setup ignored an active uninstaller'
    foreach ($image in @('MyCodex.exe', 'runtime\unins000.exe', 'unins001.exe')) {
        Assert ((Guard @($image) -UninstallerPath $uninstaller -IncludeManagedProcesses) -eq 'mycodex_resource_still_in_use') 'Uninstall exception ignored another image'
    }
    foreach ($path in @((Join-Path ($install + '-other') 'unins000.exe'), (Join-Path $install 'MyCodex.exe'))) {
        Assert ((Guard @('MyCodex.exe') -UninstallerPath $path) -eq 'invalid_uninstaller_path') 'Arbitrary image exclusion accepted'
    }
    Guard @() | Out-Null
    Assert ((Get-Item $report).Length -eq 0) 'Stale blockers retained after successful retry'
    $many = @(1..25 | ForEach-Object { "runtime\codex\$_\codex.exe" })
    Guard $many | Out-Null
    Assert (@(Get-Content $report).Count -eq 20) 'Report is not bounded'

    # Stage-specific exceptions keep their previous meaning.
    foreach ($managed in @('runtime\auth-core\mycodex-auth-host.exe', 'runtime\relay\mycodex-relay.exe', 'runtime\test\mycodex-auth-host.exe', 'relay\mycodex-relay.exe')) {
        Assert (-not (Test-MyCodexGuardedImage $managed)) "Initial guard bypassed dedicated maintenance: $managed"
        Assert (Test-MyCodexGuardedImage $managed -IncludeManagedProcesses) "Final guard ignored managed backend: $managed"
    }
    $node = 'runtime\node\v22.22.3\win-x64\node.exe'
    Assert (Test-MyCodexGuardedImage $node) 'Initial guard ignored legacy Node'
    Assert (-not (Test-MyCodexGuardedImage $node -AllowLegacyNodeCleanup)) 'Legacy cleanup gate rejected the retired runtime'
    Assert (Test-MyCodexGuardedImage $node -AllowLegacyNodeCleanup -IncludeManagedProcesses) 'Final guard bypassed remaining Node'
    foreach ($live in @('MyCodex.exe', 'runtime\codex\codex.exe', 'runtime\node-other\node.exe', 'runtime\cua\node.exe', 'runtime\git\cmd\git.exe')) {
        Assert (Test-MyCodexGuardedImage $live -AllowLegacyNodeCleanup) "Legacy cleanup bypassed a live image: $live"
    }
    foreach ($path in @((Join-Path ($work+'-other') 'runtime\node\node.exe'), (Join-Path $work 'runtime\node-other\node.exe'), (Join-Path $work 'runtime\node\..\other\node.exe'), (Join-Path $work 'runtime\node\other.exe'))) {
        Assert (-not (Test-MyCodexLegacyNodePath $path $work)) 'Legacy Node path scope escaped its directory or executable name'
    }

    # A real running image from this installation blocks; it is released after exit.
    $runtime = Join-Path $install 'runtime\codex\fixture'
    New-Item -ItemType Directory -Path $runtime -Force | Out-Null
    $probe = Join-Path $runtime 'codex.exe'
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\PING.EXE') -Destination $probe
    [IO.File]::WriteAllText((Join-Path $install 'notes.txt'), 'not an image')
    $readOnly = Join-Path $install 'readonly.dll'
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\PING.EXE') -Destination $readOnly
    Set-ItemProperty -LiteralPath $readOnly -Name IsReadOnly -Value $true
    $outside = Join-Path $work 'outside'
    New-Item -ItemType Directory -Path $outside -Force | Out-Null
    $outsideProbe = Join-Path $outside 'MyCodex.exe'
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\PING.EXE') -Destination $outsideProbe
    New-Item -ItemType Junction -Path (Join-Path $install 'linked') -Target $outside | Out-Null
    Assert (@(Get-MyCodexLockedImages -Root $install).Count -eq 0) 'Idle installation reported locked images'
    $running = Start-Process -FilePath $probe -ArgumentList '-n 30 127.0.0.1' -WindowStyle Hidden -PassThru
    $elsewhere = Start-Process -FilePath $outsideProbe -ArgumentList '-n 30 127.0.0.1' -WindowStyle Hidden -PassThru
    try {
        Start-Sleep -Milliseconds 500
        $locked = @(Get-MyCodexLockedImages -Root $install)
        Assert ($locked.Count -eq 1 -and $locked[0] -eq 'runtime\codex\fixture\codex.exe') ('Running image not reported exactly: ' + ($locked -join ', '))
        Assert (Test-MyCodexImageLocked $probe) 'Single-image probe missed a running image'
        Assert (-not (Test-MyCodexImageLocked $readOnly)) 'A read-only file was reported as running'
        $code = Invoke-GuardScript
        Assert ($code -eq 1) "Running installation image did not block: exit $code"
        Assert ((Get-Content $report -Raw).Contains('in_use path=runtime\codex\fixture\codex.exe')) 'Script report missed the running image'
    } finally {
        foreach ($process in @($running, $elsewhere)) { try { $process.Kill(); $process.WaitForExit() } catch { } ; $process.Dispose() }
    }
    Start-Sleep -Milliseconds 300
    $code = Invoke-GuardScript
    Assert ($code -eq 0) "Exited image still blocked: exit $code"
    Write-Output 'PASS: locked images block, idle and read-only files pass, linked folders and outside processes are ignored, stage exceptions preserved, bounded relative report.'
} finally {
    if (Test-Path -LiteralPath (Join-Path $install 'linked')) { [IO.Directory]::Delete((Join-Path $install 'linked')) }
    if (Test-Path -LiteralPath $work) {
        Get-ChildItem -LiteralPath $work -Recurse -File | ForEach-Object { $_.IsReadOnly = $false }
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
