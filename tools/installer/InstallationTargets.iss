// Shared with target startup; held by Setup until all files have been replaced.
var
  InstallationMutex: THandle;
  InstallationMutexRoot: String;
  ResourceDiagnosticPath: String;
  ResourceDiagnosticDetails: String;

function OpenInstallationMutex(Attributes: Integer; InitialOwner: Boolean; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function WaitInstallationMutex(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function ReleaseInstallationMutex(Handle: THandle): Boolean;
  external 'ReleaseMutex@kernel32.dll stdcall';
function CloseInstallationMutex(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function MaintenanceHelper(Name: String): String;
begin
  if IsUninstaller() then Result := ExpandConstant('{app}\tools\') + Name
  else
  begin
    ExtractTemporaryFile(Name);
    Result := ExpandConstant('{tmp}\') + Name;
  end;
end;

function UninstallerGuardArguments(): String;
begin
  Result := '';
  if IsUninstaller() then Result := ' -UninstallerPath "' + ExpandConstant('{uninstallexe}') + '"';
end;

procedure SetTargetMaintenanceStatus(Text: String);
begin
  Log('Target maintenance: ' + Text);
  if IsUninstaller() then
  begin
    UninstallProgressForm.StatusLabel.Caption := Text;
    UninstallProgressForm.Repaint;
  end
  else
  begin
    WizardForm.PreparingLabel.Caption := Text;
    WizardForm.Repaint;
  end;
end;

procedure ReleaseInstallationReservation();
begin
  if InstallationMutex <> 0 then
  begin
    ReleaseInstallationMutex(InstallationMutex);
    CloseInstallationMutex(InstallationMutex);
    InstallationMutex := 0;
    InstallationMutexRoot := '';
  end;
end;

function RunResourceGuard(ExtraArguments: String): Integer;
var
  Arguments: String;
  Lines: TArrayOfString;
  Index: Integer;
begin
  ResourceDiagnosticDetails := '';
  ResourceDiagnosticPath := ExpandConstant('{localappdata}\MyCodex\installer-diagnostics\') + GetMD5OfString(ExpandConstant('{app}')) + '\resource-users.txt';
  Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + MaintenanceHelper('Test-MyCodexResourceUsers.ps1') + '" -InstallRoot "' + ExpandConstant('{app}') + '"' + ExtraArguments + UninstallerGuardArguments();
  DeleteFile(ResourceDiagnosticPath);
  if ForceDirectories(ExtractFileDir(ResourceDiagnosticPath)) then
    Arguments := Arguments + ' -DiagnosticPath "' + ResourceDiagnosticPath + '"';
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, Result) then
    Result := 2;
  Log(Format('Installation resource guard: result=%d', [Result]));
  if LoadStringsFromFile(ResourceDiagnosticPath, Lines) then
    for Index := 0 to GetArrayLength(Lines) - 1 do
    begin
      Log('Resource owner: ' + Lines[Index]);
      if Index < 3 then ResourceDiagnosticDetails := ResourceDiagnosticDetails + #13#10 + Lines[Index];
    end;
end;

function ResourceGuardMessage(Code: Integer): String;
var
  DiagnosticLocation: String;
begin
  if Code = 0 then Result := ''
  else if Code = 1 then Result := CustomMessage('ResourceUsersRunning')
  else Result := CustomMessage('ResourceInspectionFailed');
  if Code <> 0 then
  begin
    if FileExists(ResourceDiagnosticPath) then DiagnosticLocation := ResourceDiagnosticPath
    else DiagnosticLocation := ExpandConstant('{log}');
    Result := Result + ResourceDiagnosticDetails + #13#10 + FmtMessage(CustomMessage('ResourceDiagnosticLocation'), [DiagnosticLocation]);
  end;
end;

function ReserveInstallation(): Boolean;
var
  Name: AnsiString;
  WaitResult: LongWord;
begin
  Result := False;
  // The deferred update helper already holds this mutex while waiting for Setup.
  if IsDeferredUpdate() then begin Result := True; Exit; end;
  if (InstallationMutex <> 0) and (InstallationMutexRoot = ExpandConstant('{app}')) then
  begin Result := True; Exit; end;
  ReleaseInstallationReservation();
  if RunResourceGuard(' -MutexNamePath "' + ExpandConstant('{tmp}\installation-mutex.txt') + '"') <> 0 then Exit;
  if not LoadStringFromFile(ExpandConstant('{tmp}\installation-mutex.txt'), Name) then Exit;
  InstallationMutex := OpenInstallationMutex(0, False, String(Name));
  if InstallationMutex = 0 then Exit;
  WaitResult := WaitInstallationMutex(InstallationMutex, 1000);
  Result := (WaitResult = 0) or (WaitResult = $80);
  if Result then InstallationMutexRoot := ExpandConstant('{app}')
  else begin CloseInstallationMutex(InstallationMutex); InstallationMutex := 0; end;
end;

function TryForceInstallationTargets(): Boolean;
var
  Code: Integer;
  Arguments: String;
begin
  Result := False;
  // Never convert a silent or cancelled normal installation into an implicit forced exit.
  if IsDeferredUpdate() then Exit;
  if (not IsUninstaller() and WizardSilent()) or (IsUninstaller() and UninstallSilent()) then
  begin
    if ExpandConstant('{param:MYCODEXFORCECLOSE|0}') <> '1' then Exit;
  end
  else if SuppressibleMsgBox(CustomMessage('ForceCloseTargets'), mbConfirmation,
      MB_YESNO or MB_DEFBUTTON2, IDNO) <> IDYES then Exit;
  SetTargetMaintenanceStatus(CustomMessage('PreparingForceClose'));
  Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + MaintenanceHelper('Stop-MyCodexManagedProcesses.ps1') + '" -InstallRoot "' + ExpandConstant('{app}') + '" -ForceClients' + UninstallerGuardArguments();
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, Code) then Code := 2;
  Log(Format('Explicit installation force-close: result=%d', [Code]));
  Result := Code = 0;
end;

function PrepareInstallationTargets(): String;
var
  Code: Integer;
  Attempt: Integer;
  Capability: AnsiString;
  SupportsExit: Boolean;
  ForceAttempted: Boolean;
  Arguments: String;
begin
  ForceAttempted := False;
  Result := CustomMessage('InstallationReservationFailed');
  if not ReserveInstallation() then Exit;
  Code := RunResourceGuard('');
  Result := ResourceGuardMessage(Code);
  if Code <> 1 then Exit;
  SupportsExit := False;
  // A downgrade or manual executable replacement must not retain stale capability.
  if LoadStringFromFile(ExpandConstant('{app}\tools\installation-exit.version'), Capability) and
      FileExists(ExpandConstant('{app}\MyCodex.exe')) then
    SupportsExit := CompareText(Trim(String(Capability)), '1:' + GetSHA256OfFile(ExpandConstant('{app}\MyCodex.exe'))) = 0;
  SetTargetMaintenanceStatus(CustomMessage('PreparingTargetExit'));
  Result := ResourceGuardMessage(2);
  if IsDeferredUpdate() then Code := 0 // Updater already completed save/exit; the guard below still checks actual exit.
  else if SupportsExit then
  begin
    if not Exec(ExpandConstant('{app}\MyCodex.exe'), '--exit-for-install', '', SW_HIDE, ewWaitUntilTerminated, Code) then Code := 2;
  end
  else
  begin
    MaintenanceHelper('LegacyTargetExit.cs');
    Log('Installation target exit: legacy normal-close compatibility');
    Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + MaintenanceHelper('Close-LegacyMyCodexTargets.ps1') + '" -InstallRoot "' + ExpandConstant('{app}') + '"';
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, Code) then Code := 2;
  end;
  Log(Format('Installation target exit: result=%d', [Code]));
  // A save/cancel refusal must stop maintenance, even when recovery was requested.
  if Code = 3 then begin Result := CustomMessage('TargetExitCancelled'); Exit; end;
  if (Code <> 0) and (Code <> 4) then
  begin
    ForceAttempted := True;
    if TryForceInstallationTargets() then Code := 0;
  end;
  if (Code <> 0) and (Code <> 4) then Exit;
  // Code 4 means normal exit was committed but is still draining. It must get
  // the same final process check as a successful coordinator, not fail early.
  // Targets can finish before their normally exiting child processes release files.
  for Attempt := 1 to 10 do
  begin
    Code := RunResourceGuard(' -AllowLegacyNodeCleanup');
    if Code <> 1 then Break;
    Sleep(250);
  end;
  if (Code = 1) and not ForceAttempted then
    if TryForceInstallationTargets() then Code := RunResourceGuard(' -AllowLegacyNodeCleanup');
  Result := ResourceGuardMessage(Code);
  if Code <> 0 then Exit;
  Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + MaintenanceHelper('Stop-MyCodexManagedProcesses.ps1') + '" -InstallRoot "' + ExpandConstant('{app}') + '" -LegacyNodeOnly' + UninstallerGuardArguments();
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, Code) then Code := 2;
  Log(Format('Legacy bundled Node cleanup: result=%d', [Code]));
  if Code <> 0 then
  begin
    Result := CustomMessage('ManagedProcessesStopFailed');
    Exit;
  end;
  Result := ResourceGuardMessage(RunResourceGuard(''));
end;

procedure WriteInstallationExitCapability();
begin
  if FileExists(ExpandConstant('{app}\MyCodex.exe')) then
  begin
    ForceDirectories(ExpandConstant('{app}\tools'));
    if not SaveStringToFile(ExpandConstant('{app}\tools\installation-exit.version'),
        '1:' + GetSHA256OfFile(ExpandConstant('{app}\MyCodex.exe')), False) then
      Log('Could not record automatic target exit support.');
  end;
end;
