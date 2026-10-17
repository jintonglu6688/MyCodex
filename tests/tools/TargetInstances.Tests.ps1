param([Parameter(Mandatory = $true)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$testRoot = Join-Path $repo ('artifacts\target-instance-probe-' + [Guid]::NewGuid().ToString('N'))
$bin = Join-Path $testRoot 'bin'
$data = Join-Path $testRoot 'data'
$channel = 'Probe-' + [Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path $bin, $data -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references = @('Infrastructure', 'Application', 'Domain', 'Shared') | ForEach-Object {
    $source = Join-Path $BuildDirectory ($_ + '.dll')
    Copy-Item -LiteralPath $source -Destination $bin
    '/reference:' + (Join-Path $bin ($_ + '.dll'))
}
$probe = Join-Path $bin 'TargetInstanceProbe.exe'
& $compiler /nologo /target:exe /platform:x64 ('/out:' + $probe) /reference:System.Web.Extensions.dll @references (Join-Path $PSScriptRoot 'TargetInstanceProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }

function Wait-Until([scriptblock]$Condition, [string]$Message) {
    $deadline = [DateTime]::UtcNow.AddSeconds(12)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 50
    }
    throw $Message
}
function Start-Owner([string]$Target, [string]$Marker, [string]$Executable = $probe) {
    Start-Process -FilePath $Executable -ArgumentList @('owner', ('"' + $data + '"'), $channel, $Target, $Marker) -PassThru -WindowStyle Hidden
}
function Request([string]$Mode, [string]$Target, [string]$Marker, [string]$Executable = $probe, [string]$OwnerMarker = '') {
    if ($OwnerMarker) { & $Executable $Mode $data $channel $Target $Marker $OwnerMarker }
    else { & $Executable $Mode $data $channel $Target $Marker }
    if ($LASTEXITCODE -ne 0) {
        $errorFile = Join-Path $data ($Marker + '.error')
        if (Test-Path -LiteralPath $errorFile) { throw ('Probe request failed: ' + $Marker + ': ' + [IO.File]::ReadAllText($errorFile)) }
        throw ('Probe request failed: ' + $Marker)
    }
    Get-Content -LiteralPath (Join-Path $data ($Marker + '.result')) -Raw | ConvertFrom-Json
}

$children = @()
$markers = @('first', 'duplicate', 'second', 'closing', 'recovered', 'exit-owner', 'slow-owner', 'record-loss-owner', 'record-loss-contender', 'runtime-user')
try {
    $children += Start-Owner 'windows' 'first'
    $children += Start-Owner 'windows' 'duplicate'
    Wait-Until { (Test-Path (Join-Path $data 'first.owner')) -and (Test-Path (Join-Path $data 'duplicate.owner')) } 'Concurrent owners did not report.'
    $primary = @('first', 'duplicate') | Where-Object { (Get-Content (Join-Path $data ($_ + '.owner'))) -eq 'True' }
    if (@($primary).Count -ne 1) { throw 'Duplicate target owners were admitted.' }
    $primary = [string]$primary
    Wait-Until { Test-Path (Join-Path $data ($primary + '.ready')) } 'Primary did not become ready.'
    $children += Start-Owner 'wsl-two' 'second'
    Wait-Until { Test-Path (Join-Path $data 'second.ready') } 'Independent target did not become ready.'

    $status = Request 'status' 'windows' 'status'
    if (!$status.Success -or !$status.Status.IsWorking -or !$status.Status.HasPendingInteraction -or $status.Status.IsConnected) { throw 'Status did not preserve owner-supplied details.' }
    if (!(Request 'activate' 'windows' 'activation').Success) { throw 'Cross-process activation failed.' }
    if ((Get-Content (Join-Path $data ($primary + '.request'))) -ne 'C:\probe workspace') { throw 'Workspace activation payload changed.' }
    if (!(Request 'settings' 'windows' 'settings').Success) { throw 'Cross-process settings failed.' }
    if ((Get-Content (Join-Path $data ($primary + '.request'))) -ne 'Probe renamed target') { throw 'Finite runtime edit was not delivered.' }
    & $probe oversized $data $channel windows malformed
    if ($LASTEXITCODE -ne 0 -or !(Request 'status' 'windows' 'after-malformed').Success) { throw 'Oversized frame broke listener recovery.' }

    $otherInstallation = Join-Path $bin 'OtherInstallationProbe.exe'
    Copy-Item -LiteralPath $probe -Destination $otherInstallation
    $mismatch = Request 'status' 'windows' 'other-installation' $otherInstallation
    if ($mismatch.Success -or $mismatch.Error -notlike '*another installation*') { throw 'Installation mismatch was not reported.' }

    Set-Content -LiteralPath (Join-Path $data ($primary + '.close')) -Value 'close'
    Wait-Until { (Request 'status' 'windows' 'closing-status').Status.Lifecycle -eq 2 } 'Owner did not report closing.'
    if ((Request 'activate' 'windows' 'closing-activation').Success) { throw 'Closing owner accepted activation.' }
    & $probe reserve $data $channel windows reservation
    if ((Get-Content (Join-Path $data 'reservation.result')) -ne 'occupied') { throw 'Closing owner lost the offline-edit gate.' }
    $children += Start-Owner 'windows' 'closing'
    Wait-Until { Test-Path (Join-Path $data 'closing.owner') } 'Closing contender did not report.'
    if ((Get-Content (Join-Path $data 'closing.owner')) -ne 'False') { throw 'Closing owner admitted a new instance.' }

    # Crash only our own isolated probe process to exercise native mutex abandonment.
    $ownerProcess = $children | Where-Object { $_.Id -eq $status.Instance.ProcessId }
    $ownerProcess.Kill()
    $ownerProcess.WaitForExit(5000) | Out-Null
    $children += Start-Owner 'windows' 'recovered'
    Wait-Until { Test-Path (Join-Path $data 'recovered.ready') } 'Abandoned owner could not recover.'
    $stale = Request 'stale' 'windows' 'stale-owner'
    if ($stale.Success -or !(Request 'status' 'windows' 'replacement-survivor').Success -or (Test-Path (Join-Path $data 'recovered.committed'))) { throw 'Stale exit operation closed the replacement owner.' }
    if (!(Request 'status' 'wsl-two' 'survivor').Status.IsWorking) { throw 'Another target lost its work state.' }
    Set-Content -LiteralPath (Join-Path $data 'recovered.exit') -Value 'exit'
    Wait-Until { $children[-1].HasExited } 'Recovered owner did not exit.'
    & $probe reserve $data $channel windows afterexit
    if ((Get-Content (Join-Path $data 'afterexit.result')) -ne 'reserved') { throw 'Normal exit retained the gate.' }

    $children += Start-Owner 'exit-target' 'exit-owner'
    $exitOwner = $children[-1]
    Wait-Until { Test-Path (Join-Path $data 'exit-owner.ready') } 'Exit-flow owner did not become ready.'
    $exit = Request 'exitflow' 'exit-target' 'exit-flow' $probe 'exit-owner'
    if (!$exit.Success) { throw 'Commit acknowledgement was not returned.' }
    Wait-Until { $exitOwner.HasExited } 'Committed fixture owner did not exit normally.'
    if (!(Test-Path (Join-Path $data 'exit-owner.committed'))) { throw 'After-response fixture callback did not run.' }
    & $probe reserve $data $channel exit-target committed-gate
    if ($LASTEXITCODE -ne 0 -or (Get-Content (Join-Path $data 'committed-gate.result')) -ne 'reserved') { throw 'Committed owner retained its target gate.' }

    Set-Content -LiteralPath (Join-Path $data 'slow-owner.slow') -Value 'hold preparation'
    $children += Start-Owner 'slow-target' 'slow-owner'
    $slowOwner = $children[-1]
    Wait-Until { Test-Path (Join-Path $data 'slow-owner.ready') } 'Slow-preparation owner did not become ready.'
    $cancel = Request 'slowflow' 'slow-target' 'slow-flow' $probe 'slow-owner'
    if (!$cancel.Success -or $cancel.Status.IsExitPrepared -or !$cancel.Status.IsWorking -or $slowOwner.HasExited -or (Test-Path (Join-Path $data 'slow-owner.committed'))) { throw 'Cancelled slow preparation changed work or closed its owner.' }
    # Only this copied fixture executable has the real product process name; its directory is isolated.
    $namedProbe = Join-Path $bin 'MyCodex.Dev.exe'
    Copy-Item -LiteralPath $probe -Destination $namedProbe
    $children += Start-Owner 'record-loss-target' 'record-loss-owner' $namedProbe
    $recordLossOwner = $children[-1]
    Wait-Until { Test-Path (Join-Path $data 'record-loss-owner.ready') } 'Record-loss owner did not become ready.'
    if (!(Request 'recordloss' 'record-loss-target' 'record-loss' $probe 'record-loss-owner').Success -or $recordLossOwner.HasExited) { throw 'The live fixture process was not detected after record loss.' }
    $children += Start-Owner 'record-loss-target' 'record-loss-contender' $namedProbe
    Wait-Until { Test-Path (Join-Path $data 'record-loss-contender.owner') } 'Record-loss contender did not report.'
    if ((Get-Content (Join-Path $data 'record-loss-contender.owner')) -ne 'False') { throw 'Missing metadata admitted a second live owner.' }
    $runtimeBin = Join-Path $bin 'runtime'
    New-Item -ItemType Directory -Path $runtimeBin -Force | Out-Null
    foreach ($assembly in @('Infrastructure', 'Application', 'Domain', 'Shared')) {
        Copy-Item -LiteralPath (Join-Path $bin ($assembly + '.dll')) -Destination $runtimeBin
    }
    $runtimeProbe = Join-Path $runtimeBin 'codex.exe'
    Copy-Item -LiteralPath $probe -Destination $runtimeProbe
    $children += Start-Process -FilePath $runtimeProbe -ArgumentList @('hold', ('"' + $data + '"'), $channel, 'runtime-fixture', 'runtime-user') -PassThru -WindowStyle Hidden
    $runtimeUser = $children[-1]
    Wait-Until { Test-Path (Join-Path $data 'runtime-user.ready') } 'Unrecorded runtime fixture did not become ready.'
    if (!(Request 'runtime-usage' 'runtime-fixture' 'runtime-detection' $probe 'runtime-user').Success -or $runtimeUser.HasExited) { throw 'The actual uncoordinated runtime process was not detected.' }
    if (!(Request 'status' 'wsl-two' 'after-exit-flows').Status.IsWorking) { throw 'Exit flow changed another target.' }
    Write-Output ('PASS: isolated native concurrent open, target isolation, authenticated status/activation/settings, oversized frame recovery, installation mismatch, closing exclusion, crash recovery, offline reservation; real process IPC prepare/abort/commit, stale-instance exclusion, acknowledgement before fixture closure, late slow-preparation cancellation, actual process detection/duplicate exclusion after record loss, and WMI detection of an unrecorded runtime fixture. Owners/runtime callbacks are fixtures, not WinForms/WSL/SSH/Codex integration. Evidence: ' + $testRoot)
}
finally {
    foreach ($marker in $markers) { Set-Content -LiteralPath (Join-Path $data ($marker + '.exit')) -Value 'exit' }
    foreach ($child in $children) {
        if (!$child.HasExited -and !$child.WaitForExit(5000)) { $child.Kill(); $child.WaitForExit(5000) | Out-Null }
        $child.Dispose()
    }
}
