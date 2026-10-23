param([string]$IsccPath = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$work = Join-Path $repo ('artifacts\uninstall-targets-' + [Guid]::NewGuid().ToString('N'))
$bin = Join-Path $work 'installed'
$data = Join-Path $work 'user-data'
New-Item -ItemType Directory -Path $work, $data -Force | Out-Null
$probe = Join-Path $work 'MyCodex.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /platform:x64 ('/out:' + $probe) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'LegacyTargetProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
$production = [IO.File]::ReadAllText((Join-Path $repo 'tools\installer\MyCodex.iss'))
# Compile the actual production uninstall events, with only authentication RPC stubbed.
$events = $production.Substring($production.IndexOf('function InitializeUninstall'))
$messages = ($production -split '\r?\n' | Where-Object { $_.StartsWith('english.') }) -replace '^english\.', ''
$fixture = @"
[Setup]
AppName=MyCodex isolated uninstall test
AppVersion=1
DefaultDirName=$bin
PrivilegesRequired=lowest
CreateUninstallRegKey=no
OutputDir=$work
OutputBaseFilename=uninstall-test
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
[Files]
Source: "$probe"; DestDir: "{app}"
Source: "$repo\tools\installer\Test-MyCodexResourceUsers.ps1"; DestDir: "{app}\tools"
Source: "$repo\tools\installer\Stop-MyCodexManagedProcesses.ps1"; DestDir: "{app}\tools"
Source: "$repo\tools\installer\Close-LegacyMyCodexTargets.ps1"; DestDir: "{app}\tools"
Source: "$repo\tools\installer\LegacyTargetExit.cs"; DestDir: "{app}\tools"
[CustomMessages]
$($messages -join "`r`n")
[Code]
#include "$repo\tools\installer\InstallationTargets.iss"
function AuthenticationMaintenance(Action, ScriptPath, StatePath: String): Boolean;
begin
  Log('Authentication test maintenance: ' + Action);
  Result := True;
end;
$events
"@
$fixturePath = Join-Path $work 'uninstall-test.iss'
[IO.File]::WriteAllText($fixturePath, $fixture, [Text.UTF8Encoding]::new($false))
& $IsccPath /Q $fixturePath
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller compilation failed.' }
$legacyEvents = @'
function InitializeUninstall(): Boolean;
var Code: Integer; Arguments: String;
begin
  Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\tools\Test-MyCodexResourceUsers.ps1') + '" -InstallRoot "' + ExpandConstant('{app}') + '"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := Result and (Code = 0);
end;
'@
$legacyFixture = $fixture.Replace($events, $legacyEvents).Replace('OutputBaseFilename=uninstall-test', 'OutputBaseFilename=legacy-test')
$legacyPath = Join-Path $work 'legacy-test.iss'
[IO.File]::WriteAllText($legacyPath, $legacyFixture, [Text.UTF8Encoding]::new($false))
& $IsccPath /Q $legacyPath
if ($LASTEXITCODE -ne 0) { throw 'Legacy uninstaller compilation failed.' }
function Install([string]$Name = 'uninstall-test') {
    $process = Start-Process -FilePath (Join-Path $work ($Name + '.exe')) -ArgumentList ('/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="' + $bin + '"') -PassThru -WindowStyle Hidden
    try { if (!$process.WaitForExit(20000) -or $process.ExitCode -ne 0) { throw 'Fixture install failed.' } } finally { $process.Dispose() }
}
function Uninstall([string]$Name, [switch]$ForceClose) {
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + (Join-Path $work ($Name + '.log')) + '"'
    if ($ForceClose) { $arguments += ' /MYCODEXFORCECLOSE=1' }
    $uninstaller = Get-ChildItem -LiteralPath $bin -Filter 'unins*.exe' | Where-Object { Test-Path ([IO.Path]::ChangeExtension($_.FullName, '.dat')) } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $process = Start-Process -FilePath $uninstaller.FullName -ArgumentList $arguments -PassThru -WindowStyle Hidden
    try {
        if (!$process.WaitForExit(60000)) { throw 'Uninstaller did not finish.' }
        return $process.ExitCode
    } finally { $process.Dispose() }
}
function Start-Client([string]$Name, [string]$Executable) {
    $process = Start-Process -FilePath $Executable -ArgumentList @(('"' + $data + '"'), $Name) -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (!(Test-Path (Join-Path $data ($Name + '.ready')))) {
        if ($process.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'Client did not start.' }
        Start-Sleep -Milliseconds 50
    }
    return $process
}
$children = @()
try {
    [IO.File]::WriteAllText((Join-Path $data 'preserve.txt'), 'user content')
    Install 'legacy-test'
    if ((Uninstall 'legacy-self-lock') -eq 0 -or !(Test-Path (Join-Path $bin 'MyCodex.exe'))) { throw 'Legacy self-lock was not reproduced.' }
    # Cover an actual upgrade of Inno's existing uninstall log/code, not just a clean install.
    Install
    if ((Uninstall 'idle') -ne 0 -or (Test-Path (Join-Path $bin 'MyCodex.exe'))) { throw "Idle uninstall blocked itself; inspect $work\idle.log" }
    Install
    $children += Start-Client 'normal' (Join-Path $bin 'MyCodex.exe')
    if ((Uninstall 'normal') -ne 0 -or !$children[-1].HasExited -or !(Test-Path (Join-Path $data 'normal.saved'))) { throw 'Uninstall did not save and close the running client.' }
    Install
    [IO.File]::WriteAllText((Join-Path $data 'reject.reject'), 'user cancelled saving')
    $children += Start-Client 'reject' (Join-Path $bin 'MyCodex.exe')
    if ((Uninstall 'save-cancelled' -ForceClose) -eq 0 -or $children[-1].HasExited -or
        !(Test-Path (Join-Path $bin 'MyCodex.exe')) -or !(Test-Path (Join-Path $data 'reject.close-request'))) { throw 'Uninstall force recovery bypassed an explicit save refusal.' }
    Remove-Item -LiteralPath (Join-Path $data 'reject.reject')
    if ((Uninstall 'save-retried') -ne 0 -or !$children[-1].HasExited -or !(Test-Path (Join-Path $data 'reject.saved'))) { throw 'Normal uninstall did not recover after cancellation.' }
    Install
    [IO.File]::WriteAllText((Join-Path $data 'blocked.disabled'), 'modal update failure')
    $children += Start-Client 'blocked' (Join-Path $bin 'MyCodex.exe')
    $children += Start-Client 'foreign' $probe
    if ((Uninstall 'declined') -eq 0 -or $children[-2].HasExited -or !(Test-Path (Join-Path $bin 'MyCodex.exe'))) { throw 'Uninstall bypassed default force refusal or deleted files.' }
    if ((Uninstall 'forced' -ForceClose) -ne 0 -or !$children[-2].HasExited -or $children[-1].HasExited -or (Test-Path (Join-Path $bin 'MyCodex.exe'))) { throw 'Confirmed uninstall did not close only its blocked client and remove installation.' }
    if ([IO.File]::ReadAllText((Join-Path $data 'preserve.txt')) -ne 'user content') { throw 'Uninstall touched user data.' }
    Write-Output "PASS: actual uninstall events; legacy self-lock reproduced and repaired by upgrade, idle uninstall, normal save/exit, cancellation before deletion, explicit force recovery, other installation and user data preserved. Evidence: $work"
} finally {
    foreach ($name in @('normal', 'reject', 'blocked', 'foreign')) { [IO.File]::WriteAllText((Join-Path $data ($name + '.exit')), 'fixture cleanup') }
    foreach ($process in $children) {
        if (!$process.HasExited -and !$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
        $process.Dispose()
    }
}
