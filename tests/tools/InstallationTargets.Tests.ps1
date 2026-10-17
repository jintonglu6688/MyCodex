param([Parameter(Mandatory = $true)][string]$BuildDirectory,
    [string]$IsccPath = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$work = Join-Path $repo ('artifacts\installation-exit-' + [Guid]::NewGuid().ToString('N'))
$bin = Join-Path $work 'installed'
$data = Join-Path $work 'data'
$source = Join-Path $work 'source'
New-Item -ItemType Directory -Path $bin, $data, $source -Force | Out-Null
$references = @('Infrastructure', 'Application', 'Domain', 'Shared') | ForEach-Object {
    Copy-Item -LiteralPath (Join-Path $BuildDirectory ($_ + '.dll')) -Destination $bin
    '/reference:' + (Join-Path $bin ($_ + '.dll'))
}
$probe = Join-Path $bin 'MyCodex.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /platform:x64 ('/out:' + $probe) /reference:System.Web.Extensions.dll @references (Join-Path $PSScriptRoot 'TargetInstanceProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
Copy-Item -LiteralPath $probe -Destination (Join-Path $bin 'MyCodex.Dev.exe')
[IO.File]::WriteAllText((Join-Path $bin 'probe-root.txt'), $data)
New-Item -ItemType Directory -Path (Join-Path $bin 'tools') -Force | Out-Null
$capabilityPath = Join-Path $bin 'tools\installation-exit.version'
$capability = '1:' + (Get-FileHash -LiteralPath $probe -Algorithm SHA256).Hash
[IO.File]::WriteAllText($capabilityPath, $capability)
[IO.File]::WriteAllText((Join-Path $source 'installed.txt'), 'replacement was allowed')
New-Item -ItemType Directory -Path (Join-Path $source 'runtime\auth-core') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $source 'runtime\auth-core\mycodex-auth-host.manifest.json'), '{}')

# Compile the complete production script without building/downloading a release payload.
& $IsccPath /Q ('/DSourceDir=' + $source) ('/DOutputDir=' + $work) /DMaximumInstallRootLength=180 /DAuthenticationInstallTest=MyCodexExitCompileTest (Join-Path $repo 'tools\installer\MyCodex.iss')
if ($LASTEXITCODE -ne 0) { throw 'Production installer compilation failed.' }

$fixture = @"
[Setup]
AppName=MyCodex isolated graceful exit test
AppVersion=1
DefaultDirName=$bin
PrivilegesRequired=lowest
CreateUninstallRegKey=no
Uninstallable=no
DisableProgramGroupPage=yes
OutputDir=$work
OutputBaseFilename=exit-test
CloseApplications=no
RestartApplications=no
[Files]
Source: "$source\installed.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "$repo\tools\installer\Test-MyCodexResourceUsers.ps1"; Flags: dontcopy
Source: "$repo\tools\installer\Close-LegacyMyCodexTargets.ps1"; Flags: dontcopy
Source: "$repo\tools\installer\LegacyTargetExit.cs"; Flags: dontcopy
Source: "$repo\tools\installer\Stop-MyCodexManagedProcesses.ps1"; Flags: dontcopy
[CustomMessages]
ResourceUsersRunning=RESOURCE_BUSY
ResourceInspectionFailed=INSPECTION_FAILED
ResourceDiagnosticLocation=PROCESS_DIAGNOSTICS: %1
ManagedProcessesStopFailed=MANAGED_STOP_FAILED
InstallationReservationFailed=RESERVATION_FAILED
PreparingTargetExit=PREPARING_EXIT
TargetExitCancelled=EXIT_CANCELLED
ForceCloseTargets=FORCE_CLOSE_WARNING
PreparingForceClose=PREPARING_FORCE_CLOSE
[Code]
function IsDeferredUpdate(): Boolean;
begin Result := ExpandConstant('{param:MYCODEXDEFERRED|0}') = '1'; end;
#include "$repo\tools\installer\InstallationTargets.iss"
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  ExtractTemporaryFile('Test-MyCodexResourceUsers.ps1');
  Result := PrepareInstallationTargets();
  if Result <> '' then ReleaseInstallationReservation();
end;
procedure CurStepChanged(Step: TSetupStep);
var Code: Integer;
begin
  if Step = ssInstall then begin
    Code := RunResourceGuard('');
    if Code <> 0 then RaiseException(ResourceGuardMessage(Code));
  end;
  if Step = ssPostInstall then begin WriteInstallationExitCapability(); ReleaseInstallationReservation(); end;
end;
procedure DeinitializeSetup();
begin ReleaseInstallationReservation(); end;
"@
$fixturePath = Join-Path $work 'exit-test.iss'
[IO.File]::WriteAllText($fixturePath, $fixture, [Text.UTF8Encoding]::new($false))
& $IsccPath /Q $fixturePath
if ($LASTEXITCODE -ne 0) { throw 'Installer fixture compilation failed.' }

function Wait-Until([scriptblock]$Condition, [string]$Message) {
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 100
    }
    throw $Message
}
function Start-Owner([string]$Target, [string]$Executable = $probe, [string]$Channel = 'InstallProbe') {
    Start-Process -FilePath $Executable -ArgumentList @('owner', ('"' + $data + '"'), $Channel, $Target, $Target) -PassThru -WindowStyle Hidden
}
function Start-Installer([string]$Name, [switch]$Deferred, [switch]$ForceClose) {
    $arguments = @('/SP-', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="' + $bin + '"'), ('/LOG="' + (Join-Path $work ($Name + '.log')) + '"'))
    if ($Deferred) { $arguments += '/MYCODEXDEFERRED=1' }
    if ($ForceClose) { $arguments += '/MYCODEXFORCECLOSE=1' }
    Start-Process -FilePath (Join-Path $work 'exit-test.exe') -ArgumentList $arguments -PassThru -WindowStyle Hidden
}
$children = @()
try {
    # Compare the native installation mutex with the production .NET identity.
    . (Join-Path $repo 'tools\installer\Test-MyCodexResourceUsers.ps1') -InstallRoot $bin -FunctionsOnly
    $mutexName = Get-MyCodexUpdateMutexName $bin
    $children += Start-Owner 'first'
    $children += Start-Owner 'second' (Join-Path $bin 'MyCodex.Dev.exe') 'InstallProbeDev'
    Wait-Until { (Test-Path (Join-Path $data 'first.ready')) -and (Test-Path (Join-Path $data 'second.ready')) } 'Targets did not start.'
    & $probe activate $data InstallProbeDev second forbidden-activation
    if ($LASTEXITCODE -ne 0 -or (Get-Content (Join-Path $data 'forbidden-activation.result') -Raw | ConvertFrom-Json).Success) { throw 'Cross-edition installation permission allowed ordinary activation.' }
    [IO.File]::WriteAllText((Join-Path $data 'second.reject'), 'cancel saving')
    # Even a requested recovery must respect a later save/cancel refusal.
    $installer = Start-Installer 'cancel' -ForceClose
    if (!$installer.WaitForExit(40000)) { throw 'Cancelled installation did not finish.' }
    if ($installer.ExitCode -eq 0 -or (Test-Path (Join-Path $bin 'installed.txt')) -or $children[0].HasExited -or $children[1].HasExited) { throw 'Save refusal did not leave both targets running and block copying.' }
    if (!(Test-Path (Join-Path $data 'first.aborted')) -or !(Test-Path (Join-Path $data 'second.aborted'))) { throw 'Prepared targets were not unfrozen.' }
    if (![IO.File]::ReadAllText((Join-Path $work 'cancel.log')).Contains('EXIT_CANCELLED')) { throw 'Cancellation was reported as a running-process error.' }
    Remove-Item -LiteralPath (Join-Path $data 'second.reject')

    [IO.File]::WriteAllText($capabilityPath, '1:stale-hash-after-downgrade')
    $installer = Start-Installer 'legacy'
    if (!$installer.WaitForExit(40000) -or $installer.ExitCode -eq 0 -or $children[0].HasExited -or $children[1].HasExited) { throw 'Legacy capability did not fail safely.' }
    if (![IO.File]::ReadAllText((Join-Path $work 'legacy.log')).Contains('legacy normal-close compatibility')) { throw 'Missing capability did not try normal legacy exit.' }
    [IO.File]::WriteAllText($capabilityPath, $capability)

    # Hold a preparation to check that Setup's native mutex blocks new targets.
    [IO.File]::WriteAllText((Join-Path $data 'first.linger'), '15000') # Exceeds the coordinator's 10-second exit wait.
    [IO.File]::WriteAllText((Join-Path $data 'second.slow'), 'wait')
    Remove-Item -LiteralPath (Join-Path $data 'second.prepare-started')
    $installer = Start-Installer 'success'
    Wait-Until { Test-Path (Join-Path $data 'second.prepare-started') } 'Installer did not request preparation.'
    $mutex = [Threading.Mutex]::OpenExisting($mutexName)
    try {
        if ($mutex.WaitOne(0)) { $mutex.ReleaseMutex(); throw 'Setup did not hold the shared update mutex.' }
    } finally { $mutex.Dispose() }
    $children += Start-Owner 'blocked-start'
    if (!$children[-1].WaitForExit(5000) -or $children[-1].ExitCode -eq 0 -or (Test-Path (Join-Path $data 'blocked-start.ready'))) { throw 'Installation admitted an unprepared new target.' }
    [IO.File]::WriteAllText((Join-Path $data 'second.release'), 'save completed')
    if (!$installer.WaitForExit(40000)) { throw 'Graceful installation did not finish.' }
    if ($installer.ExitCode -ne 0 -or !(Test-Path (Join-Path $bin 'installed.txt'))) { throw 'Prepared targets did not allow installation; inspect success.log.' }
    if (![IO.File]::ReadAllText((Join-Path $work 'success.log')).Contains('Installation target exit: result=4')) { throw 'Slow-exit regression did not exercise the timeout return.' }
    foreach ($child in $children[0..1]) { if (!$child.HasExited) { throw 'Copying began before both target processes exited.' } }
    foreach ($target in @('first', 'second')) { if (!(Test-Path (Join-Path $data ($target + '.committed')))) { throw 'Normal exit was not committed.' } }
    $children += Start-Owner 'after-install'
    Wait-Until { Test-Path (Join-Path $data 'after-install.ready') } 'Installation did not release its startup lock.'
    if ([IO.File]::ReadAllText($capabilityPath) -ine $capability) { throw 'Installation did not record executable-bound capability.' }
    [IO.File]::WriteAllText((Join-Path $data 'after-install.exit'), 'fixture normal exit')
    if (!$children[-1].WaitForExit(5000)) { throw 'Fixture cleanup before deferred installation failed.' }
    $mutex = [Threading.Mutex]::new($false, $mutexName)
    try {
        if (!$mutex.WaitOne(0)) { throw 'Installation retained mutex ownership.' }
        try {
            $installer = Start-Installer 'deferred' -Deferred
            if (!$installer.WaitForExit(40000) -or $installer.ExitCode -ne 0) { throw 'Deferred installation deadlocked or failed against the updater-owned mutex.' }
        } finally { $mutex.ReleaseMutex() }
    } finally { $mutex.Dispose() }

    # Genuine legacy shape: no IPC records, no maintenance CLI, no capability marker.
    Remove-Item -LiteralPath $capabilityPath
    Remove-Item -LiteralPath (Join-Path $bin 'installed.txt')
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /platform:x64 ('/out:' + $probe) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'LegacyTargetProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Legacy fixture compilation failed.' }
    Copy-Item -LiteralPath $probe -Destination (Join-Path $bin 'MyCodex.Dev.exe') -Force
    $other = Join-Path $work 'other-installation'
    New-Item -ItemType Directory -Path $other | Out-Null
    Copy-Item -LiteralPath $probe -Destination $other
    $nodeSource = Join-Path $work 'NodeProbe.cs'
    [IO.File]::WriteAllText($nodeSource, 'using System; using System.IO; using System.Threading; class Probe { static void Main(string[] args) { File.WriteAllText(args[0]+".ready", "ready"); var end=DateTime.UtcNow.AddMinutes(5); while(!File.Exists(args[0]+".exit") && DateTime.UtcNow<end) Thread.Sleep(50); } }')
    $nodeBinary = Join-Path $work 'node.exe'
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe ('/out:' + $nodeBinary) $nodeSource
    if ($LASTEXITCODE -ne 0) { throw 'Node fixture compilation failed.' }
    $nodeChildren = @()
    foreach ($spec in @(@('node-first', (Join-Path $bin 'runtime\node\v22.22.3\win-x64')), @('node-second', (Join-Path $bin 'runtime\node\v22.22.3\win-x64')), @('node-foreign', (Join-Path ($bin + '-other') 'runtime\node\v22.22.3\win-x64')), @('node-system', (Join-Path $work 'system-node')))) {
        New-Item -ItemType Directory -Path $spec[1] -Force | Out-Null
        $binary = Join-Path $spec[1] 'node.exe'
        if (!(Test-Path $binary)) { Copy-Item -LiteralPath $nodeBinary -Destination $binary }
        $marker = [string]$spec[0]
        $nodeChildren += Start-Process -FilePath $binary -ArgumentList ('"' + (Join-Path $data $marker) + '"') -PassThru -WindowStyle Hidden
        Wait-Until { Test-Path (Join-Path $data ($marker + '.ready')) } 'Node fixture did not start.'
    }
    $children += $nodeChildren
    [IO.File]::WriteAllText((Join-Path $data 'legacy-first.sync'), '7.0 synchronous FormClosing')
    foreach ($spec in @(@('legacy-first', $probe), @('legacy-reject', (Join-Path $bin 'MyCodex.Dev.exe')), @('unrelated', (Join-Path $other 'MyCodex.exe')))) {
        $children += Start-Process -FilePath $spec[1] -ArgumentList @(('"' + $data + '"'), $spec[0]) -PassThru -WindowStyle Hidden
        $marker = [string]$spec[0]
        Wait-Until { Test-Path (Join-Path $data ($marker + '.ready')) } 'Legacy window did not initialize.'
    }
    [IO.File]::WriteAllText((Join-Path $data 'legacy-reject.reject'), 'user cancelled')
    $installer = Start-Installer 'legacy-cancel' -ForceClose
    if (!$installer.WaitForExit(40000) -or $installer.ExitCode -eq 0 -or (Test-Path (Join-Path $bin 'installed.txt'))) { throw 'Legacy refusal failed to block copying.' }
    if (!(Test-Path (Join-Path $data 'legacy-reject.close-request')) -or $children[-2].HasExited) { throw 'Legacy cancellation was bypassed.' }
    if ($children[-1].HasExited -or (Test-Path (Join-Path $data 'unrelated.close-request'))) { throw 'Another installation was touched.' }
    if (@($nodeChildren | Where-Object HasExited).Count -ne 0) { throw 'Node cleanup bypassed save cancellation.' }
    Remove-Item -LiteralPath (Join-Path $data 'legacy-reject.reject')
    $installer = Start-Installer 'legacy-success'
    if (!$installer.WaitForExit(40000) -or $installer.ExitCode -ne 0 -or !(Test-Path (Join-Path $bin 'installed.txt'))) { throw 'Running old clients still block installation; inspect legacy-success.log.' }
    foreach ($marker in @('legacy-first', 'legacy-reject')) {
        if (!(Test-Path (Join-Path $data ($marker + '.saved')))) { throw 'Legacy normal save was bypassed.' }
    }
    if (!$children[-2].HasExited -or !$children[-3].HasExited -or $children[-1].HasExited) { throw 'Legacy process isolation or exit waiting failed.' }
    if (!$nodeChildren[0].HasExited -or !$nodeChildren[1].HasExited -or $nodeChildren[2].HasExited -or $nodeChildren[3].HasExited) { throw 'Legacy Node cleanup did not preserve installation boundaries.' }
    $unrelatedClient = $children[-1]
    # The reported machine may already have closed its main window. An orphan
    # still needs cleanup, but invoking the cleanup script without Setup's gate must fail.
    $children += Start-Process -FilePath (Join-Path $bin 'runtime\node\v22.22.3\win-x64\node.exe') -ArgumentList ('"' + (Join-Path $data 'node-orphan') + '"') -PassThru -WindowStyle Hidden
    Wait-Until { Test-Path (Join-Path $data 'node-orphan.ready') } 'Orphan Node fixture did not start.'
    $start = [Diagnostics.ProcessStartInfo]::new('powershell.exe')
    $start.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $repo 'tools\installer\Stop-MyCodexManagedProcesses.ps1') + '" -InstallRoot "' + $bin + '" -LegacyNodeOnly'
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.RedirectStandardError = $true
    $unreserved = [Diagnostics.Process]::Start($start)
    try {
        $errors = $unreserved.StandardError.ReadToEndAsync()
        if (!$unreserved.WaitForExit(15000) -or $unreserved.ExitCode -ne 2 -or $children[-1].HasExited) { throw 'Unreserved cleanup was allowed to stop Node.' }
        [IO.File]::WriteAllText((Join-Path $work 'unreserved-node.log'), $errors.Result)
    } finally { $unreserved.Dispose() }
    Remove-Item -LiteralPath $capabilityPath
    Remove-Item -LiteralPath (Join-Path $bin 'installed.txt')
    $installer = Start-Installer 'orphan-node'
    if (!$installer.WaitForExit(40000) -or $installer.ExitCode -ne 0 -or !$children[-1].HasExited -or !(Test-Path (Join-Path $bin 'installed.txt'))) { throw 'Orphan Node still blocked installation.' }
    # A standalone bundled runtime can survive an old client. Report the exact
    # blocker without touching it or misreporting a hidden main window.
    $runtime = Join-Path $bin 'runtime\codex'
    New-Item -ItemType Directory -Path $runtime -Force | Out-Null
    $runtimeSource = Join-Path $work 'RuntimeProbe.cs'
    [IO.File]::WriteAllText($runtimeSource, 'using System; using System.IO; using System.Threading; class Probe { static void Main(string[] args) { File.WriteAllText(args[0]+".ready", "ready"); var end=DateTime.UtcNow.AddMinutes(5); while(!File.Exists(args[0]+".exit") && DateTime.UtcNow<end) Thread.Sleep(50); } }')
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe ('/out:' + (Join-Path $runtime 'codex.exe')) $runtimeSource
    if ($LASTEXITCODE -ne 0) { throw 'Runtime fixture compilation failed.' }
    $children += Start-Process -FilePath (Join-Path $runtime 'codex.exe') -ArgumentList ('"' + (Join-Path $data 'runtime-residue') + '"') -PassThru -WindowStyle Hidden
    $runtimeResidue = $children[-1]
    Wait-Until { Test-Path (Join-Path $data 'runtime-residue.ready') } 'Runtime fixture did not start.'
    # This fixture installs only installed.txt, so its capability stamp does not
    # describe a replaced executable. Restore the genuine 7.0 shape for this case.
    Remove-Item -LiteralPath $capabilityPath
    Remove-Item -LiteralPath (Join-Path $bin 'installed.txt')
    $installer = Start-Installer 'runtime-residue'
    if (!$installer.WaitForExit(40000) -or $installer.ExitCode -eq 0 -or $children[-1].HasExited -or (Test-Path (Join-Path $bin 'installed.txt'))) { throw 'Unowned runtime was ignored or stopped.' }
    $blockedLog = [IO.File]::ReadAllText((Join-Path $work 'runtime-residue.log'))
    if (!$blockedLog.Contains('in_use path=runtime\codex\codex.exe') -or !$blockedLog.Contains('PROCESS_DIAGNOSTICS:')) { throw 'Runtime blocker was not reported to the user and log.' }
    if (!$blockedLog.Contains('RESOURCE_BUSY') -or !$blockedLog.Contains('legacy normal-close compatibility')) { throw 'Runtime test did not exercise legacy compatibility.' }
    # Simulate the old client's blocked update dialog: its main window is disabled.
    # An unrelated installation and an unowned bundled runtime are still running.
    [IO.File]::WriteAllText((Join-Path $data 'force-blocked.disabled'), 'modal update failure')
    $children += Start-Process -FilePath $probe -ArgumentList @(('"' + $data + '"'), 'force-blocked') -PassThru -WindowStyle Hidden
    $blockedClient = $children[-1]
    Wait-Until { Test-Path (Join-Path $data 'force-blocked.ready') } 'Force-close fixture did not initialize.'
    $installer = Start-Installer 'force-declined'
    if (!$installer.WaitForExit(40000) -or $installer.ExitCode -eq 0 -or $blockedClient.HasExited -or (Test-Path (Join-Path $bin 'installed.txt'))) { throw 'Force-close default did not preserve the blocked client.' }
    $unreservedForce = [Diagnostics.ProcessStartInfo]::new('powershell.exe')
    $unreservedForce.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $repo 'tools\installer\Stop-MyCodexManagedProcesses.ps1') + '" -InstallRoot "' + $bin + '" -ForceClients'
    $unreservedForce.UseShellExecute = $false; $unreservedForce.CreateNoWindow = $true; $unreservedForce.RedirectStandardError = $true
    $forceProbe = [Diagnostics.Process]::Start($unreservedForce)
    try {
        $forceErrors = $forceProbe.StandardError.ReadToEndAsync()
        if (!$forceProbe.WaitForExit(15000) -or $forceProbe.ExitCode -ne 2 -or $blockedClient.HasExited) { throw 'Unreserved force-close must return inspection failure and leave the client running.' }
        [IO.File]::WriteAllText((Join-Path $work 'unreserved-force.log'), $forceErrors.Result)
    } finally { $forceProbe.Dispose() }
    if ($runtimeResidue.HasExited -or $unrelatedClient.HasExited) { throw 'Force-close fixtures exited before the confirmation test.' }
    $installer = Start-Installer 'force-confirmed' -ForceClose
    if (!$installer.WaitForExit(40000) -or $installer.ExitCode -ne 0 -or !$blockedClient.HasExited -or !(Test-Path (Join-Path $bin 'installed.txt'))) { throw 'Explicit force-close did not install past the blocked old client.' }
    if ((Test-Path (Join-Path $data 'force-blocked.saved'))) { throw 'Force-close case unexpectedly used normal save; regression not exercised.' }
    foreach ($marker in @('node-foreign', 'node-system', 'unrelated')) {
        if ((Test-Path (Join-Path $data ($marker + '.close-request')))) { throw 'Force-close touched an unrelated installation.' }
    }
    if (!$runtimeResidue.HasExited) { throw 'Force-close did not stop the installation-owned runtime.' }
    foreach ($child in @($nodeChildren[2], $nodeChildren[3], $unrelatedClient)) { if ($child.HasExited) { throw 'Force-close stopped another installation or system runtime.' } }
    if (![IO.File]::ReadAllText((Join-Path $work 'force-confirmed.log')).Contains('Explicit installation force-close: result=0')) { throw 'Force-close was not recorded in Setup log.' }
    Write-Output ('PASS: production Inno compilation; two-edition real IPC prepare/cancel/commit; no copying or closing on refusal; activation isolation; legacy/downgrade protection; native installer mutex blocks startup and is released; deferred installer retains updater-owned gate. Evidence: ' + $work)
    Write-Output 'PASS: explicitly confirmed installer force-close ends a blocked modal client and installation-owned runtime, preserves other installations/runtimes, refuses an unreserved invocation, and copies only after resource release.'
    Write-Output 'PASS: running old-style hidden WinForms targets without an installer entry save and close automatically; refusal blocks copying; unrelated installation stays running.'
    Write-Output 'PASS: two legacy bundled Node processes cleaned only after client save/exit; cancellation preserves them; foreign and system Node stay running.'
} finally {
    if ($installer -and !$installer.HasExited) { $installer.Kill(); $installer.WaitForExit(5000) | Out-Null }
    foreach ($target in @('first', 'second', 'blocked-start', 'after-install', 'legacy-first', 'legacy-reject', 'unrelated', 'runtime-residue', 'node-first', 'node-second', 'node-foreign', 'node-system', 'node-orphan', 'force-blocked')) {
        [IO.File]::WriteAllText((Join-Path $data ($target + '.exit')), 'fixture cleanup')
    }
    foreach ($child in $children) {
        if (!$child.HasExited -and !$child.WaitForExit(5000)) { $child.Kill(); $child.WaitForExit(5000) | Out-Null }
        $child.Dispose()
    }
}
