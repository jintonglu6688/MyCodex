param(
    [string]$BuildDirectory,
    [string]$IsccPath = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $repo 'tests\MyCodex.Application.Tests\bin\Debug' }
$work = Join-Path $repo ('artifacts\authentication-installer-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $work 'source'
$package = Join-Path $source 'runtime\auth-core'
New-Item -ItemType Directory -Path $package -Force | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
function Write-Json($path, $value) { [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 8), $utf8) }
function Assert($condition, $message) { if (-not $condition) { throw $message } }

# Both executables are fixtures. The maintenance entry delegates to production C#;
# the backend emits fixed identities, never uses accounts/network, and never stays running.
$references = @('Infrastructure', 'Application', 'Domain', 'Shared') | ForEach-Object {
    $assembly = Join-Path $BuildDirectory ($_ + '.dll')
    Assert (Test-Path -LiteralPath $assembly) 'Build the application tests before running installer tests.'
    Copy-Item -LiteralPath $assembly -Destination $source
    Copy-Item -LiteralPath $assembly -Destination $package
    '/reference:' + $assembly
}
$application = Join-Path $source 'MyCodex.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /platform:x64 /reference:System.Web.Extensions.dll @references ('/out:' + $application) (Join-Path $PSScriptRoot 'AuthenticationMaintenanceProbe.cs')
Assert ($LASTEXITCODE -eq 0) 'Fixture compilation failed'
$exe = Join-Path $package 'mycodex-auth-host.exe'
Copy-Item -LiteralPath $application -Destination $exe
[IO.File]::WriteAllText((Join-Path $package 'LICENSE'), 'fixture', $utf8)
$manifest = @{schemaVersion=1; hostVersion='0.4.1'; protocolVersion=3; upstreamRevision='85ad4969b3399500baaea9d46b1e08f87aba80e1'; sourceRevision=('a'*40); sourceDirty=$false; target='x86_64-pc-windows-msvc'; executable='mycodex-auth-host.exe'; sha256=(Get-FileHash $exe).Hash.ToLowerInvariant()}
Write-Json (Join-Path $package 'mycodex-auth-host.manifest.json') $manifest

function Invoke-Maintenance($root, $action) {
    $arguments = @('--authentication-maintenance', $action, ('"' + $root + '"'), ('"' + (Join-Path $root 'state.json') + '"'))
    $process = Start-Process -FilePath (Join-Path $root 'MyCodex.exe') -ArgumentList $arguments -PassThru -WindowStyle Hidden
    try {
        Assert ($process.WaitForExit(70000)) 'Compiled maintenance timed out'
        return $process.ExitCode
    } finally { $process.Dispose() }
}
function New-Install($name) {
    $root = Join-Path $work $name
    Copy-Item -LiteralPath $source -Destination $root -Recurse
    Write-Json (Join-Path $root 'state.json') @{installRoot=$root; targets=@(); hadPackage=$true; completed=$false}
    Copy-Item -LiteralPath (Join-Path $package 'mycodex-auth-host.manifest.json') -Destination (Join-Path $root 'state.json.installed')
    return $root
}

$valid = New-Install 'valid'
Assert ((Invoke-Maintenance $valid 'Finalize') -eq 0) 'Valid package did not finalize'
Assert ((Get-Content (Join-Path $valid 'state.json') -Raw | ConvertFrom-Json).completed) 'Valid finalization did not complete'

# The actual WinForms entry must work from Setup's minimal extracted dependency set.
$actual = New-Install 'actual-application'
Copy-Item -LiteralPath (Join-Path $BuildDirectory 'MyCodex.exe') -Destination (Join-Path $actual 'MyCodex.exe') -Force
Assert ((Invoke-Maintenance $actual 'Finalize') -eq 0) 'Actual application maintenance entry requires an unpackaged dependency'

$corrupt = New-Install 'corrupt'
[IO.File]::WriteAllText((Join-Path $corrupt 'runtime\auth-core\mycodex-auth-host.exe'), 'incomplete', $utf8)
Assert ((Invoke-Maintenance $corrupt 'Finalize') -eq 1) 'Corrupt package was accepted'
Assert ((Invoke-Maintenance $corrupt 'Resume') -eq 1) 'Recovery accepted corrupt installed files'

$deferred = New-Install 'deferred'
$target = @{home=(Join-Path $deferred 'home'); store=(Join-Path $deferred 'store'); processId=0}
New-Item -ItemType Directory -Path $target.home -Force | Out-Null
Write-Json (Join-Path $deferred 'state.json') @{installRoot=$deferred; targets=@($target); hadPackage=$true; completed=$false}
$failure = Join-Path $deferred 'runtime\auth-core\startup-fail'
[IO.File]::WriteAllText($failure, 'fixture', $utf8)
Assert ((Invoke-Maintenance $deferred 'Finalize') -eq 2) 'Unavailable backend did not retain deferred recovery'
Assert (-not (Get-Content (Join-Path $deferred 'state.json') -Raw | ConvertFrom-Json).completed) 'Deferred recovery was discarded'
Remove-Item -LiteralPath $failure
Assert ((Invoke-Maintenance $deferred 'Resume') -eq 0) 'Next recovery did not start the backend'
Assert ((Get-Content (Join-Path $deferred 'state.json') -Raw | ConvertFrom-Json).completed) 'Recovery did not complete'

$older = New-Install 'older-protocol-cancellation'
$olderManifest = $manifest.Clone()
$olderManifest.hostVersion = '0.2.1'; $olderManifest.protocolVersion = 2
$olderManifest.upstreamRevision = 'e0f70019b2758f5b6b9a04dd60e4689481a0c0ac'
Write-Json (Join-Path $older 'runtime\auth-core\mycodex-auth-host.manifest.json') $olderManifest
Remove-Item -LiteralPath (Join-Path $older 'state.json.installed')
$olderTarget = @{home=(Join-Path $older 'home'); store=(Join-Path $older 'store'); processId=0}
New-Item -ItemType Directory -Path $olderTarget.home -Force | Out-Null
Write-Json (Join-Path $older 'state.json') @{installRoot=$older; targets=@($olderTarget); hadPackage=$true; completed=$false}
Assert ((Invoke-Maintenance $older 'Resume') -eq 0) 'Cancellation failed to restore a verified older protocol backend'

# Execute the complete production installer with a unique identity and fixture payload.
function Test-Installer($name, $interrupted) {
    $output = Join-Path $work $name
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    & $IsccPath /Q ('/DSourceDir=' + $source) ('/DOutputDir=' + $output) /DMaximumInstallRootLength=180 ('/DAuthenticationInstallTest=AuthTest' + [Guid]::NewGuid().ToString('N')) (Join-Path $repo 'tools\installer\MyCodex.iss')
    Assert ($LASTEXITCODE -eq 0) 'Installer compilation failed'
    $root = Join-Path $output 'installed'
    $installer = Join-Path $output 'MyCodex_v3.0.0_Setup.exe'
    $process = Start-Process -FilePath $installer -ArgumentList @('/SP-', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="'+$root+'"'), ('/LOG="'+$output+'\setup.log"')) -PassThru -WindowStyle Hidden
    try {
        Assert ($process.WaitForExit(120000)) 'Installer timed out'
        Assert ($process.ExitCode -eq 0) ("Installer $name returned " + $process.ExitCode)
    } finally { $process.Dispose() }
    $log = Get-Content -LiteralPath (Join-Path $output 'setup.log') -Raw
    Assert ($log.Contains('action=Prepare result=0 started=1')) 'Compiled preparation did not run'
    $expected = if ($interrupted) { 'action=Finalize result=-1 started=1' } else { 'action=Finalize result=0 started=1' }
    Assert ($log.Contains($expected)) ('Wrong finalization outcome: ' + $name)
    Assert (-not $log.Contains('action=Resume')) 'Installed files triggered pre-install rollback'
    if ($interrupted) {
        $diagnosticPath = [regex]::Match($log, 'action=Finalize result=-1 started=1 diagnostics=([^\r\n]+)').Groups[1].Value
        $statePath = $diagnosticPath.Substring(0, $diagnosticPath.Length - 4)
        Assert (Test-Path -LiteralPath ($statePath + '.installed')) 'Failed helper lost the expected package identity'
        Assert (-not (Get-Content -LiteralPath $diagnosticPath -Raw).Contains('Finalize started')) 'Fixture did not reproduce interruption before the first Finalize log'
        $resume = Start-Process -FilePath (Join-Path $root 'MyCodex.exe') -ArgumentList '--resume-authentication-hosts' -PassThru -WindowStyle Hidden
        try { Assert ($resume.WaitForExit(70000) -and $resume.ExitCode -eq 0) 'Installed application could not recover pending work' }
        finally { $resume.Dispose() }
        Assert ((Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).completed) 'Pending installation was not recovered'
    }
}
Test-Installer 'success' $false
[IO.File]::WriteAllText((Join-Path $source 'abort-finalize'), 'fixture', $utf8)
Test-Installer 'helper-interrupted' $true
Remove-Item -LiteralPath (Join-Path $source 'abort-finalize')
[IO.File]::WriteAllText((Join-Path $package 'version-fail'), 'fixture', $utf8)
Test-Installer 'no-install-time-backend-probe' $false

# Exercise the actual static hash gate independently of any running backend.
$iss = [IO.File]::ReadAllText((Join-Path $repo 'tools\installer\MyCodex.iss'))
$begin = $iss.IndexOf('function AuthenticationFilesInstalled(): Boolean;', [StringComparison]::Ordinal)
$end = $iss.IndexOf('procedure CurStepChanged(', $begin, [StringComparison]::Ordinal)
$check = $iss.Substring($begin, $end - $begin)
$integrityScript = @"
[Setup]
AppName=MyCodex isolated authentication integrity test
AppVersion=1
DefaultDirName=$work\integrity-installed
PrivilegesRequired=lowest
CreateUninstallRegKey=no
Uninstallable=no
DisableProgramGroupPage=yes
OutputDir=$work
OutputBaseFilename=integrity-test
[Files]
Source: "$package\mycodex-auth-host.exe"; DestDir: "{tmp}"; Flags: dontcopy
Source: "$package\mycodex-auth-host.manifest.json"; DestDir: "{tmp}"; Flags: dontcopy
Source: "$package\LICENSE"; DestDir: "{tmp}"; Flags: dontcopy
Source: "$package\*"; DestDir: "{app}\runtime\auth-core"; Flags: ignoreversion
[Code]
var Valid: Boolean;
$check
procedure CurStepChanged(Step: TSetupStep);
var Scenario: String;
begin
  if Step <> ssPostInstall then Exit;
  ExtractTemporaryFile('mycodex-auth-host.exe');
  ExtractTemporaryFile('mycodex-auth-host.manifest.json');
  ExtractTemporaryFile('LICENSE');
  Scenario := ExpandConstant('{param:CASE|valid}');
  if Scenario = 'missing' then DeleteFile(ExpandConstant('{app}\runtime\auth-core\mycodex-auth-host.exe'));
  if Scenario = 'changed' then SaveStringToFile(ExpandConstant('{app}\runtime\auth-core\mycodex-auth-host.exe'), 'changed', False);
  if Scenario = 'manifest' then SaveStringToFile(ExpandConstant('{app}\runtime\auth-core\mycodex-auth-host.manifest.json'), '{}', False);
  Valid := AuthenticationFilesInstalled();
end;
function GetCustomSetupExitCode(): Integer;
begin
  if Valid then Result := 0 else Result := 10;
end;
"@
$integrityPath = Join-Path $work 'integrity-test.iss'
[IO.File]::WriteAllText($integrityPath, $integrityScript, $utf8)
& $IsccPath /Q $integrityPath
Assert ($LASTEXITCODE -eq 0) 'Static integrity fixture did not compile'
foreach ($scenario in @('valid', 'missing', 'changed', 'manifest')) {
    $child = Start-Process -FilePath (Join-Path $work 'integrity-test.exe') -ArgumentList @('/SP-', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/CASE=' + $scenario)) -PassThru -WindowStyle Hidden
    try {
        Assert ($child.WaitForExit(60000)) 'Static integrity fixture timed out'
        $expected = if ($scenario -eq 'valid') { 0 } else { 10 }
        Assert ($child.ExitCode -eq $expected) ('Wrong static integrity decision: ' + $scenario)
    } finally { $child.Dispose() }
}
Write-Host 'PASS: compiled maintenance, integrity failures, deferred restart/recovery, and installer success when Finalize exits -1 before logging.'
Write-Host ('Artifacts: ' + $work)
