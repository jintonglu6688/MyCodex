#ifndef MyCodexVersion
  #define MyCodexVersion "3.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\..\artifacts\package\MyCodex"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\artifacts\installer"
#endif

#ifndef MaximumInstallRootLength
  #error MaximumInstallRootLength is required; use tools/installer/build-installer.ps1.
#endif

[Setup]
#ifdef AuthenticationInstallTest
AppId={#AuthenticationInstallTest}
CreateUninstallRegKey=no
#else
AppId={{1C2A4E62-3C4A-4D6B-AB9A-2F6B6A0F6A21}
#endif
AppName=MyCodex
AppVersion={#MyCodexVersion}
AppVerName=MyCodex {#MyCodexVersion}
DefaultDirName={localappdata}\Programs\MyCodex
DefaultGroupName=MyCodex
UninstallDisplayIcon={app}\MyCodex.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
WizardStyle=classic
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=2
LZMAUseSeparateProcess=yes
OutputDir={#OutputDir}
OutputBaseFilename=MyCodex_v{#MyCodexVersion}_Setup
DisableProgramGroupPage=yes
DisableDirPage=no
CloseApplications=no
RestartApplications=no
ChangesAssociations=no
UsePreviousAppDir=yes
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
UsePreviousLanguage=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[CustomMessages]
english.InstallPathTooLong=The installation folder is too long for the bundled files. Choose a shorter path (at most %1 characters).
chinesesimp.InstallPathTooLong=安装目录过长，无法完整安装所带组件。请选择更短的路径（最多 %1 个字符）。
english.ResourceUsersRunning=A target or shared runtime is still using installation files. Close it and retry. Setup cannot replace files until all resource users have stopped.
chinesesimp.ResourceUsersRunning=目标或共享运行时仍在使用安装文件。请关闭后重试；相关进程退出前，安装程序不会替换文件。
english.ManagedProcessesStopFailed=Setup could not stop a backend process owned by this installation. Finish pending work and retry. Provide the Setup log if the problem persists.
chinesesimp.ManagedProcessesStopFailed=无法停止属于本安装目录的后台进程。请完成待处理工作后重试；若仍失败，请提供安装日志。
english.AuthenticationMaintenanceFailed=Authentication backend maintenance failed. Finish sign-in or active requests, then retry. Before uninstalling, switch any converted provider to a direct provider. Close any legacy authentication backend after switching it to a direct provider. No backend was forcibly stopped.
chinesesimp.AuthenticationMaintenanceFailed=认证后台维护失败。请完成登录或在途请求后重试；卸载前请将转换供应商切换为直连配置。若旧版认证后台仍在运行，请先切换直连并退出旧版后台。程序没有强制停止认证后台。
english.AuthenticationRecoveryFailed=Authentication routing could not be restored. The previous backend package was retained for recovery. Check the authentication center before using a converted provider.
chinesesimp.AuthenticationRecoveryFailed=认证路由未能恢复，先前的后台包已保留用于恢复。使用转换供应商前请检查认证中心。
english.AuthenticationVerificationFailed=Installation did not complete: the authentication backend could not be verified or restored. MyCodex was not started. Retry installation. Setup log: %1
chinesesimp.AuthenticationVerificationFailed=安装未完成：认证后台版本校验或恢复失败，未启动 MyCodex。请重新运行安装程序。安装日志：%1
english.VerifyingAuthentication=Verifying the installed authentication backend and restoring routing...
chinesesimp.VerifyingAuthentication=正在校验已安装的认证后台并恢复路由……
english.InstallationIncomplete=Installation incomplete
chinesesimp.InstallationIncomplete=安装未完成

english.ResourceInspectionFailed=Setup could not check whether the installation folder is in use. This does not mean MyCodex is running. Retry or provide the Setup log (/LOG) for diagnosis.
chinesesimp.ResourceInspectionFailed=安装程序无法检查安装目录是否正在使用，这不代表 MyCodex 正在运行。请重试，或提供安装日志（/LOG）协助排查。
english.PreparingCheckInstallation=Checking whether MyCodex is running from the installation folder...
chinesesimp.PreparingCheckInstallation=正在检查安装目录中的 MyCodex 是否正在运行……
english.PreparingAuthentication=Preparing the authentication backend for the update...
chinesesimp.PreparingAuthentication=正在准备更新认证后台……
english.PreparingBackgroundProcesses=Stopping background processes of this installation...
chinesesimp.PreparingBackgroundProcesses=正在停止本安装目录的后台进程……
english.ResourceDiagnosticLocation=Process diagnostics (can be copied and sent for support): %1
chinesesimp.ResourceDiagnosticLocation=进程诊断文件（可复制内容反馈）：%1
english.InstallationReservationFailed=Another installation or update is in progress, or the installation lock could not be acquired. Wait for it to finish and retry.
chinesesimp.InstallationReservationFailed=其他安装或更新正在进行，或无法取得安装锁。请等待其结束后重试。
english.PreparingTargetExit=Saving and exiting all targets using this installation. If MyCodex asks to exit or save, confirm or save to continue; cancelling will stop installation.
chinesesimp.PreparingTargetExit=正在保存并退出使用此安装目录的全部目标。若 MyCodex 显示退出确认或保存对话框，请完成确认或保存以继续；取消将中止安装。
english.TargetExitCancelled=MyCodex did not exit: exit was cancelled, saving failed, a dialog is still open, or exit was not confirmed in time. Installation was stopped; close any open MyCodex dialog, finish the save or pending operation, and retry.
chinesesimp.TargetExitCancelled=MyCodex 未能退出：可能是退出被取消、保存失败、仍有对话框打开，或未及时确认退出。安装已中止；请关闭 MyCodex 中打开的对话框，完成保存或待处理操作后重试。
english.ForceCloseTargets=MyCodex could not close normally. Force close all MyCodex windows and bundled runtime processes from this installation and continue?%n%nThis stops running tasks and may lose unsaved content. Other installation folders will not be affected.%n%nChoose No to cancel forced closure and keep the remaining processes running.
chinesesimp.ForceCloseTargets=MyCodex 无法正常退出。是否强制关闭本安装目录的全部 MyCodex 窗口和内置运行时，并继续？%n%n这会中断正在运行的任务，未保存的内容可能丢失。不会关闭其他安装目录的程序。%n%n选择“否”将取消强制关闭，保留尚在运行的进程。
english.PreparingForceClose=Force closing this installation's clients and runtimes after your confirmation...
chinesesimp.PreparingForceClose=正在按您的确认强制关闭本安装目录的客户端和运行时……

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
Type: filesandordirs; Name: "{app}\runtime\*"

[UninstallDelete]
Type: files; Name: "{app}\tools\installation-exit.version"

[Files]
; Setup helpers come first, in extraction order. With solid compression, extracting a file
; decompresses everything stored before it; placed after the payload, each helper cost
; several seconds of silent waiting before installation started. Duplicate sources share
; their first storage location, so the installed copies below reuse this small chunk.
Source: "Test-MyCodexResourceUsers.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "Close-LegacyMyCodexTargets.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "LegacyTargetExit.cs"; DestDir: "{tmp}"; Flags: dontcopy
Source: "Stop-MyCodexManagedProcesses.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "Update-AuthenticationHosts.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "..\authentication-center\Test-AuthenticationHostPackage.ps1"; DestDir: "{tmp}"; Flags: dontcopy
Source: "{#SourceDir}\runtime\auth-core\mycodex-auth-host.manifest.json"; DestDir: "{tmp}"; DestName: "expected-authentication.json"; Flags: dontcopy
Source: "Test-MyCodexResourceUsers.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "Stop-MyCodexManagedProcesses.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "Close-LegacyMyCodexTargets.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "LegacyTargetExit.cs"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "Update-AuthenticationHosts.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs solidbreak

[Icons]
#ifndef AuthenticationInstallTest
Name: "{autoprograms}\MyCodex"; Filename: "{app}\MyCodex.exe"
Name: "{autodesktop}\MyCodex"; Filename: "{app}\MyCodex.exe"; Tasks: desktopicon
#endif

[Run]
#ifndef AuthenticationInstallTest
Filename: "{app}\MyCodex.exe"; Description: "{cm:LaunchProgram,MyCodex}"; Flags: nowait postinstall skipifsilent; Check: CanLaunchInstalledApplication
#endif

[Code]
var
  AuthenticationPrepared: Boolean;
  AuthenticationInstalled: Boolean;
  AuthenticationFinalizationFailed: Boolean;
  AuthenticationStatePath: String;

function CheckInstallPath(): String;
begin
  if Length(ExpandConstant('{app}')) > {#MaximumInstallRootLength} then
    Result := FmtMessage(CustomMessage('InstallPathTooLong'), ['{#MaximumInstallRootLength}'])
  else
    Result := '';
end;

function CanLaunchInstalledApplication(): Boolean;
begin
  Result := AuthenticationInstalled;
end;

#include "InstallationTargets.iss"

function AuthenticationMaintenance(Action: String; ScriptPath: String; StatePath: String): Boolean;
var
  ResultCode: Integer;
  Arguments: String;
begin
  Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" -Action ' + Action + ' -InstallRoot "' + ExpandConstant('{app}') + '" -StatePath "' + StatePath + '"';
  if Action = 'Finalize' then
    Arguments := Arguments + ' -ExpectedManifestPath "' + ExpandConstant('{tmp}\expected-authentication.json') + '" -VerificationScript "' + ExpandConstant('{tmp}\Test-AuthenticationHostPackage.ps1') + '"';
  #ifdef DevelopmentAuthenticationHost
  if Action = 'Finalize' then
    Arguments := Arguments + ' -AllowDevelopmentAuthenticationHost';
  #endif
  ResultCode := -1;
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log(Format('Authentication maintenance: action=%s result=%d diagnostics=%s.log', [Action, ResultCode, StatePath]));
  if Result then Result := ResultCode = 0;
end;

procedure RecoverAuthentication();
begin
  if not AuthenticationPrepared then Exit;
  AuthenticationPrepared := False;
  if not AuthenticationMaintenance('Resume', ExpandConstant('{tmp}\Update-AuthenticationHosts.ps1'), AuthenticationStatePath) then
    SuppressibleMsgBox(CustomMessage('AuthenticationRecoveryFailed'), mbError, MB_OK, IDOK);
end;

procedure SetPreparingStatus(const Text: String);
begin
  // Each preparation step can take a moment; say what is happening instead of a static page.
  WizardForm.PreparingLabel.Caption := Text;
  Log('Preparation step: ' + Text);
  WizardForm.Repaint;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  ScriptPath: String;
  Arguments: String;
begin
  Result := CheckInstallPath();
  if Result <> '' then Exit;
  SetPreparingStatus(CustomMessage('PreparingCheckInstallation'));
  ExtractTemporaryFile('Test-MyCodexResourceUsers.ps1');
  try
    Result := PrepareInstallationTargets();
    if Result <> '' then Exit;
    SetPreparingStatus(CustomMessage('PreparingAuthentication'));
    ExtractTemporaryFile('Update-AuthenticationHosts.ps1');
    ExtractTemporaryFile('Test-AuthenticationHostPackage.ps1');
    ExtractTemporaryFile('expected-authentication.json');
    AuthenticationStatePath := ExpandConstant('{localappdata}\MyCodex\authentication-updates\') + GetMD5OfString(ExpandConstant('{app}')) + '\state.json';
    ForceDirectories(ExtractFileDir(AuthenticationStatePath));
    AuthenticationPrepared := True;
    if not AuthenticationMaintenance('Prepare', ExpandConstant('{tmp}\Update-AuthenticationHosts.ps1'), AuthenticationStatePath) then
    begin
      Result := CustomMessage('AuthenticationMaintenanceFailed');
      Exit;
    end;
    SetPreparingStatus(CustomMessage('PreparingBackgroundProcesses'));
    ExtractTemporaryFile('Stop-MyCodexManagedProcesses.ps1');
    ScriptPath := ExpandConstant('{tmp}\Stop-MyCodexManagedProcesses.ps1');
    Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" -InstallRoot "' + ExpandConstant('{app}') + '"';
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Result := CustomMessage('ManagedProcessesStopFailed')
    else if ResultCode <> 0 then
      Result := CustomMessage('ManagedProcessesStopFailed')
    else
      Result := '';
  finally
    if Result <> '' then
    begin
      RecoverAuthentication();
      ReleaseInstallationReservation();
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  GuardError: String;
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    GuardError := ResourceGuardMessage(RunResourceGuard(' -IncludeManagedProcesses'));
    if GuardError <> '' then RaiseException(GuardError);
  end;
  if CurStep = ssPostInstall then
  begin
    AuthenticationFinalizationFailed := True;
    WizardForm.StatusLabel.Caption := CustomMessage('VerifyingAuthentication');
    try
      if AuthenticationMaintenance('Finalize', ExpandConstant('{tmp}\Update-AuthenticationHosts.ps1'), AuthenticationStatePath) then
      begin
        ResultCode := -1;
        if Exec(ExpandConstant('{app}\MyCodex.exe'), '--resume-authentication-hosts', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
          AuthenticationInstalled := ResultCode = 0;
        Log(Format('Authentication startup registrations: result=%d', [ResultCode]));
      end;
      AuthenticationFinalizationFailed := not AuthenticationInstalled;
      if AuthenticationInstalled then
      begin
        AuthenticationPrepared := False;
        WriteInstallationExitCapability();
      end
      else RecoverAuthentication();
    finally
      ReleaseInstallationReservation();
    end;
  end;
end;

procedure DeinitializeSetup();
begin
  try
    RecoverAuthentication();
  finally
    ReleaseInstallationReservation();
  end;
end;

function GetCustomSetupExitCode(): Integer;
begin
  if AuthenticationFinalizationFailed then Result := 10 else Result := 0;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and AuthenticationFinalizationFailed then
  begin
    WizardForm.FinishedHeadingLabel.Caption := CustomMessage('InstallationIncomplete');
    WizardForm.FinishedLabel.Caption := FmtMessage(CustomMessage('AuthenticationVerificationFailed'), [ExpandConstant('{log}')]);
    WizardForm.RunList.Visible := False;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  // No shutdown or mutation until the user accepts Inno's uninstall confirmation.
  Result := True;
end;

procedure CurUninstallStepChanged(Step: TUninstallStep);
var
  ResultCode: Integer;
  Arguments: String;
  GuardError: String;
begin
  if Step <> usUninstall then Exit;
  // Exceptions here abort before Inno deletes any files; Deinitialize releases the gate.
  GuardError := PrepareInstallationTargets();
  if GuardError <> '' then RaiseException(GuardError);
  SetTargetMaintenanceStatus(CustomMessage('PreparingAuthentication'));
  if not AuthenticationMaintenance('Uninstall', MaintenanceHelper('Update-AuthenticationHosts.ps1'), ExpandConstant('{tmp}\authentication-uninstall.json')) then
    RaiseException(CustomMessage('AuthenticationMaintenanceFailed'));
  SetTargetMaintenanceStatus(CustomMessage('PreparingBackgroundProcesses'));
  Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + MaintenanceHelper('Stop-MyCodexManagedProcesses.ps1') + '" -InstallRoot "' + ExpandConstant('{app}') + '"';
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then ResultCode := 2;
  if ResultCode <> 0 then RaiseException(CustomMessage('ManagedProcessesStopFailed'));
  GuardError := ResourceGuardMessage(RunResourceGuard(' -IncludeManagedProcesses'));
  if GuardError <> '' then RaiseException(GuardError);
end;

procedure DeinitializeUninstall();
begin
  ReleaseInstallationReservation();
end;
