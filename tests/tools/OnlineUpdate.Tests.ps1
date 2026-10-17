param(
    [Parameter(Mandatory = $true)][string]$ClientBuildDirectory,
    [Parameter(Mandatory = $true)][string]$NewBuildDirectory,
    [string]$IsccPath = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$work = Join-Path $repo ('artifacts\ou-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$ClientBuildDirectory = (Resolve-Path -LiteralPath $ClientBuildDirectory).Path
$NewBuildDirectory = (Resolve-Path -LiteralPath $NewBuildDirectory).Path
if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $ClientBuildDirectory 'MyCodex.exe')).ProductVersion.Trim() -ne '8.0.1' -or
    [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $NewBuildDirectory 'MyCodex.exe')).ProductVersion.Trim() -ne '8.0.2') { throw 'Expected client8.0.1 and replacement8.0.2.' }
$probe = Join-Path $work 'OnlineUpdateProbe.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /platform:x64 ('/out:' + $probe) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'OnlineUpdateProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
foreach ($mode in @('automatic', 'manual')) {
    $evidence = Join-Path $work $mode
    $installed = Join-Path $evidence 'installed'
    New-Item -ItemType Directory -Path $installed -Force | Out-Null
    Get-ChildItem -LiteralPath $ClientBuildDirectory -File | Copy-Item -Destination $installed
    Move-Item -LiteralPath (Join-Path $installed 'MyCodex.exe') -Destination (Join-Path $installed 'MyCodex.Client.exe')
    Copy-Item -LiteralPath $probe -Destination (Join-Path $installed 'MyCodex.exe')
    [IO.File]::WriteAllText((Join-Path $installed 'evidence-root.txt'), $evidence)
    New-Item -ItemType Directory -Path (Join-Path $installed 'tools') | Out-Null
    [IO.File]::WriteAllText((Join-Path $installed 'tools\installation-exit.version'), '1:' + (Get-FileHash -LiteralPath $probe).Hash)
    # Real installer coordination and replacement, without user registry, shortcuts,
    # authentication or runtime changes. Skip wizard pages for unattended acceptance.
    $fixture = @"
[Setup]
AppName=MyCodex isolated online update
AppVersion=8.0.2
DefaultDirName=$installed
PrivilegesRequired=lowest
CreateUninstallRegKey=no
Uninstallable=no
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
DisableFinishedPage=yes
CloseApplications=no
RestartApplications=no
OutputDir=$evidence
OutputBaseFilename=MyCodex_v8.0.2_Test
SetupLogging=yes
[Files]
Source: "$NewBuildDirectory\MyCodex.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "$NewBuildDirectory\*.dll"; DestDir: "{app}"; Flags: ignoreversion
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
begin Result := False; end;
#include "$repo\tools\installer\InstallationTargets.iss"
var AutoStarted: Boolean;
function PostTestClick(Window: THandle; Message, WParam, LParam: LongWord): Boolean;
  external 'PostMessageW@user32.dll stdcall';
procedure CurPageChanged(Page: Integer);
begin
  if (Page = wpReady) and not AutoStarted then begin
    AutoStarted := True;
    PostTestClick(WizardForm.NextButton.Handle, 245, 0, 0);
  end;
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  SaveStringToFile('$evidence\installer-started.txt', GetCmdTail(), False);
  SaveStringToFile('$evidence\installation-directory.txt', ExpandConstant('{app}'), False);
  Result := PrepareInstallationTargets();
  if Result <> '' then begin
    SaveStringToFile('$evidence\installer-error.txt', Result, False);
    ReleaseInstallationReservation();
  end;
end;
procedure CurStepChanged(Step: TSetupStep);
var Code: Integer;
begin
  if Step = ssInstall then begin
    Code := RunResourceGuard('');
    if Code <> 0 then RaiseException(ResourceGuardMessage(Code));
  end;
  if Step = ssPostInstall then begin
    WriteInstallationExitCapability();
    ReleaseInstallationReservation();
    SaveStringToFile('$evidence\installed.txt', '8.0.2', False);
  end;
end;
procedure DeinitializeSetup();
begin ReleaseInstallationReservation(); end;
"@
    $fixturePath = Join-Path $evidence 'update.iss'
    [IO.File]::WriteAllText($fixturePath, $fixture, [Text.UTF8Encoding]::new($false))
    & $IsccPath /Q $fixturePath
    if ($LASTEXITCODE -ne 0) { throw 'Update fixture compilation failed.' }
    $installer = Join-Path $evidence 'MyCodex_v8.0.2_Test.exe'
    $manifest = @{schemaVersion='1'; channel='stable'; app=@{version='8.0.2'; releaseNotes='Isolated internal update test'; asset=@{
        url=([uri]$installer).AbsoluteUri; fileName=[IO.Path]::GetFileName($installer); size=(Get-Item -LiteralPath $installer).Length; sha256=(Get-FileHash -LiteralPath $installer).Hash
    }}}
    $manifestPath = Join-Path $evidence 'stable.json'
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $installed 'MyCodex.exe'), $mode)
    $start.WorkingDirectory = $evidence
    $start.UseShellExecute = $false
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.CreateNoWindow = $true
    $start.EnvironmentVariables['MYCODEX_UPDATE_MANIFEST_URL'] = ([uri]$manifestPath).AbsoluteUri
    $client = [Diagnostics.Process]::Start($start)
    try {
        if (!$client.WaitForExit(150000)) { throw 'Online update client did not exit.' }
        if ($client.ExitCode -ne 0) { throw "Update probe failed; inspect $evidence\error.txt" }
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while (!(Test-Path (Join-Path $evidence 'installed.txt')) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
        foreach ($marker in @('prompt','download','saved','installer-started','installed')) {
            if (!(Test-Path (Join-Path $evidence ($marker + '.txt')))) { throw "Update stage missing: $mode/$marker; evidence=$evidence" }
        }
        if ([int](Get-Content (Join-Path $evidence 'saved.txt') -Raw) -lt 1) { throw 'Installer bypassed client save.' }
        $download = Get-Content (Join-Path $evidence 'download.txt') -Raw
        if ((Get-FileHash -LiteralPath $download).Hash -ne (Get-FileHash -LiteralPath $installer).Hash) { throw 'Downloaded installer hash mismatch.' }
        $launch = Get-Content (Join-Path $evidence 'installer-started.txt') -Raw
        $destination = Get-Content (Join-Path $evidence 'installation-directory.txt') -Raw
        if (!$launch.Contains('/SP-') -or !$launch.Contains('/DIR=') -or $destination -ine $installed) { throw 'Installer was not handed the exact installation.' }
        if ((Get-FileHash -LiteralPath (Join-Path $installed 'MyCodex.exe')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $NewBuildDirectory 'MyCodex.exe')).Hash) { throw '8.0.2 client replacement failed.' }
        Write-Output "PASS: $mode check -> real prompt -> verified download -> real installer launch -> IPC save/exit -> replacement8.0.2. Evidence: $evidence"
    }
    finally {
        if (!$client.HasExited) { $client.Kill(); $client.WaitForExit(5000) | Out-Null }
        $client.Dispose()
        # Keep failures unattended too: only this fixture's installer and its child.
        $setupParents = @(Get-CimInstance Win32_Process | Where-Object {
            $_.Name -eq 'MyCodex_v8.0.2_Test.exe' -and $_.ExecutablePath -and
            $_.ExecutablePath.StartsWith($evidence + '\', [StringComparison]::OrdinalIgnoreCase)
        })
        foreach ($parent in $setupParents) {
            foreach ($child in @(Get-CimInstance Win32_Process -Filter ('ParentProcessId = ' + $parent.ProcessId))) {
                if ($child.Name -eq 'MyCodex_v8.0.2_Test.tmp' -and $child.ExecutablePath.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) {
                    Stop-Process -Id $child.ProcessId -ErrorAction SilentlyContinue
                }
            }
            Stop-Process -Id $parent.ProcessId -ErrorAction SilentlyContinue
        }
    }
}
