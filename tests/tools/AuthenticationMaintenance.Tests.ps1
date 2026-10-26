param([string]$IsccPath = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$work = Join-Path $repo ('artifacts\authentication-installer-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $work 'source'
$package = Join-Path $source 'runtime\auth-core'
New-Item -ItemType Directory -Path $package -Force | Out-Null
$script = Join-Path $repo 'tools\installer\Update-AuthenticationHosts.ps1'
$utf8 = [Text.UTF8Encoding]::new($false)
function Write-Json($path, $value) { [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 8), $utf8) }
function Assert($condition, $message) { if (-not $condition) { throw $message } }

# No network, real accounts, routes or user backends. This executable only emits
# fixture identities and exit codes; even its serve command immediately exits.
$code = @'
using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
class Probe {
    static int Main(string[] args) {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        if (args.Length == 1 && args[0] == "--resume-authentication-hosts")
            return File.Exists(Path.Combine(root, "startup-fail")) ? 1 : 0;
        var json = new JavaScriptSerializer();
        var identity = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "mycodex-auth-host.manifest.json")));
        if (args.Length == 1 && args[0] == "--version-json") { Console.WriteLine(json.Serialize(identity)); return 0; }
        if (args[0] == "serve") { Console.Error.Write("backend_already_running"); return 1; }
        if (args[0] == "rpc") {
            Console.In.ReadToEnd();
            identity["hostVersion"] = "0.2.1";
            Console.WriteLine(json.Serialize(new { result = identity })); return 0;
        }
        return 2;
    }
}
'@
$codePath = Join-Path $work 'Probe.cs'
[IO.File]::WriteAllText($codePath, $code, $utf8)
$exe = Join-Path $package 'mycodex-auth-host.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /platform:x64 /reference:System.Web.Extensions.dll ('/out:' + $exe) $codePath
Assert ($LASTEXITCODE -eq 0) 'Fixture compilation failed'
Copy-Item -LiteralPath $exe -Destination (Join-Path $source 'MyCodex.exe')
[IO.File]::WriteAllText((Join-Path $package 'LICENSE'), 'fixture', $utf8)
$manifest = @{schemaVersion=1; hostVersion='0.3.11'; protocolVersion=3; upstreamRevision='85ad4969b3399500baaea9d46b1e08f87aba80e1'; sourceRevision=('a'*40); sourceDirty=$false; target='x86_64-pc-windows-msvc'; executable='mycodex-auth-host.exe'; sha256=(Get-FileHash $exe).Hash.ToLowerInvariant()}
Write-Json (Join-Path $package 'mycodex-auth-host.manifest.json') $manifest
$expected = Join-Path $work 'expected.json'
Write-Json $expected $manifest

function Invoke-TestPowerShell([string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new('powershell.exe')
    $info.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + ($Arguments -join '" "') + '"'
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $child = [Diagnostics.Process]::Start($info)
    try {
        $stdout = $child.StandardOutput.ReadToEndAsync(); $stderr = $child.StandardError.ReadToEndAsync()
        Assert ($child.WaitForExit(90000)) 'Maintenance timed out'
        return @{Code=$child.ExitCode; Output=($stdout.Result + $stderr.Result)}
    } finally { $child.Dispose() }
}
function Run-Maintenance($root, $action, $expectedCode = 0) {
    $result = Invoke-TestPowerShell @($script, '-Action', $action, '-InstallRoot', $root, '-StatePath', (Join-Path $root 'state.json'), '-ExpectedManifestPath', $expected)
    Assert ($result.Code -eq $expectedCode) ("Unexpected maintenance outcome: $action " + $result.Code + ' ' + $result.Output)
}
function New-Install($name) {
    $root = Join-Path $work $name
    Copy-Item -LiteralPath $source -Destination $root -Recurse
    Write-Json (Join-Path $root 'state.json') @{installRoot=$root; targets=@(); hadPackage=$true; completed=$false}
    return $root
}

$valid = New-Install 'valid'
Run-Maintenance $valid 'Finalize'
Assert ((Get-Content (Join-Path $valid 'state.json') -Raw | ConvertFrom-Json).completed) 'Valid finalization did not complete'

$development = New-Install 'development'
$developmentManifest = $manifest.Clone(); $developmentManifest.sourceDirty = $true
$developmentExpected = Join-Path $work 'development-expected.json'
Write-Json $developmentExpected $developmentManifest
Write-Json (Join-Path $development 'runtime\auth-core\mycodex-auth-host.manifest.json') $developmentManifest
$arguments = @($script, '-Action', 'Finalize', '-InstallRoot', $development, '-StatePath', (Join-Path $development 'state.json'), '-ExpectedManifestPath', $developmentExpected)
$refused = Invoke-TestPowerShell $arguments
Assert ($refused.Code -eq 1) 'Development backend was accepted by ordinary finalization'
$allowed = Invoke-TestPowerShell ($arguments + '-AllowDevelopmentAuthenticationHost')
Assert ($allowed.Code -eq 0) ('Explicit development finalization failed: ' + $allowed.Output)
Assert ((Get-Content (Join-Path $development 'state.json') -Raw | ConvertFrom-Json).completed) 'Development finalization did not complete'
[IO.File]::AppendAllText((Join-Path $development 'runtime\auth-core\mycodex-auth-host.exe'), 'tampered', $utf8)
$tampered = Invoke-TestPowerShell ($arguments + '-AllowDevelopmentAuthenticationHost')
Assert ($tampered.Code -eq 1) 'Development switch bypassed the expected payload checksum'
. (Join-Path $repo 'tools\authentication-center\Test-AuthenticationHostPackage.ps1')
$developmentReceipt = Join-Path $work 'development-setup.exe'
Copy-Item -LiteralPath $exe -Destination $developmentReceipt
Write-Json ($developmentReceipt + '.auth.json') @{schemaVersion=1; development=$true; installerSha256=$manifest.sha256; authenticationHost=$developmentManifest}
$publishAccepted = $true
try { Test-AuthenticationInstallerReceipt -InstallerPath $developmentReceipt } catch { $publishAccepted = $false }
Assert (-not $publishAccepted) 'Release publishing accepted a development installer receipt'

$old = New-Install 'old'
$oldManifest = $manifest.Clone(); $oldManifest.hostVersion = '0.2.1'
Write-Json (Join-Path $old 'runtime\auth-core\mycodex-auth-host.manifest.json') $oldManifest
Run-Maintenance $old 'Finalize' 1
Assert (-not (Get-Content (Join-Path $old 'state.json') -Raw | ConvertFrom-Json).completed) 'Old version reported success'

$corrupt = New-Install 'corrupt'
$backup = Join-Path $corrupt 'state.json.backup'
Copy-Item -LiteralPath (Join-Path $old 'runtime\auth-core') -Destination $backup -Recurse
[IO.File]::WriteAllText((Join-Path $corrupt 'runtime\auth-core\mycodex-auth-host.exe'), 'incomplete', $utf8)
Run-Maintenance $corrupt 'Finalize' 1
Assert ([IO.File]::ReadAllText((Join-Path $corrupt 'runtime\auth-core\mycodex-auth-host.exe')) -eq 'incomplete') 'Finalize silently restored an old package'
Run-Maintenance $corrupt 'Resume'
Assert ((Get-Content (Join-Path $corrupt 'runtime\auth-core\mycodex-auth-host.manifest.json') -Raw | ConvertFrom-Json).hostVersion -eq '0.2.1') 'Cancellation recovery no longer restores the old package'

$wrongArtifact = New-Install 'wrong-artifact'
$different = $manifest.Clone(); $different.sourceRevision = 'b'*40
Write-Json (Join-Path $wrongArtifact 'runtime\auth-core\mycodex-auth-host.manifest.json') $different
Run-Maintenance $wrongArtifact 'Finalize' 1

$resident = New-Install 'old-resident'
Write-Json (Join-Path $resident 'state.json') @{installRoot=$resident; targets=@(@{home=(Join-Path $resident 'home'); store=(Join-Path $resident 'store'); processId=0}); hadPackage=$true; completed=$false}
Run-Maintenance $resident 'Finalize' 1
Assert ((Get-Content (Join-Path $resident 'state.json.log') -Raw).Contains('authentication_update_identity_mismatch')) 'Old resident was not identified'

# Inject only enumeration/native lookup at the process boundary. No stop command
# can be reached: the known-path control has deliberately invalid arguments.
$wrapper = Join-Path $work 'inspect.ps1'
[IO.File]::WriteAllText($wrapper, @'
param($Script, $Root, $Scenario, $Kind = 'auth')
Add-Type 'namespace MyCodexInstaller { public static class ProcessImage { public static string Read(int id) { return System.Environment.GetEnvironmentVariable("MYCODEX_TEST_PROCESS_PATH"); } } }'
function Get-CimInstance { [pscustomobject]@{ExecutablePath=$null; ProcessId=$PID; CommandLine='fixture invalid'} }
function Invoke-CimMethod { [pscustomobject]@{ReturnValue=0; Sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value} }
# Reaching a Relay stop is observable without ever stopping an actual process.
function Stop-Process { throw 'fixture_stop_reached' }
$env:MYCODEX_TEST_PROCESS_PATH = switch($Scenario) {
    'fallback' { if ($Kind -eq 'relay') { Join-Path $Root 'runtime\relay\mycodex-relay.exe' } else { Join-Path $Root 'runtime\auth-core\mycodex-auth-host.exe' } }
    'foreign' { Join-Path $Root 'another-install\mycodex-auth-host.exe' }
    default { '' }
}
# An unreadable process is attributed to this installation only while its backend image is in use.
$held = @()
if ($Scenario -eq 'unknown-locked') {
    $relay = Join-Path $Root 'runtime\relay\mycodex-relay.exe'
    New-Item -ItemType Directory -Path (Split-Path -Parent $relay) -Force | Out-Null
    if (-not (Test-Path -LiteralPath $relay)) { Copy-Item -LiteralPath (Join-Path $Root 'runtime\auth-core\mycodex-auth-host.exe') -Destination $relay }
    foreach ($image in @((Join-Path $Root 'runtime\auth-core\mycodex-auth-host.exe'), $relay)) { $held += [IO.File]::Open($image, 'Open', 'Read', 'Read') }
}
if ($Kind -eq 'relay') { & $Script -InstallRoot $Root }
else { & $Script -Action Prepare -InstallRoot $Root -StatePath (Join-Path $Root 'state.json') }
exit $LASTEXITCODE
'@, $utf8)
foreach ($scenario in @('unknown', 'unknown-locked', 'fallback', 'foreign')) {
    $root = New-Install $scenario
    $result = Invoke-TestPowerShell @($wrapper, $script, $root, $scenario)
    $expectedCode = if ($scenario -in @('foreign', 'unknown')) { 0 } else { 1 }
    Assert ($result.Code -eq $expectedCode) ("Wrong process inspection result: $scenario " + $result.Output)
    $reason = switch ($scenario) {
        'unknown' { 'unreadable_process_ignored' }
        'unknown-locked' { 'authentication_process_identity_unavailable' }
        'fallback' { 'authentication_update_unknown_process' }
        default { $null }
    }
    if ($reason) { Assert ((Get-Content (Join-Path $root 'state.json.log') -Raw).Contains($reason)) "Wrong diagnosis: $scenario" }
    $relay = Invoke-TestPowerShell @($wrapper, (Join-Path $repo 'tools\installer\Stop-MyCodexManagedProcesses.ps1'), $root, $scenario, 'relay')
    $relayCode = switch ($scenario) { 'unknown' { 0 }; 'unknown-locked' { 2 }; 'fallback' { 1 }; 'foreign' { 0 } }
    Assert ($relay.Code -eq $relayCode) ("Wrong Relay inspection result: $scenario " + $relay.Output)
}

. (Join-Path $repo 'tools\installer\Test-MyCodexResourceUsers.ps1') -InstallRoot $valid -FunctionsOnly
foreach ($name in @('mycodex-auth-host.exe','mycodex-relay.exe')) {
    $relative = 'runtime\test\' + $name
    Assert (Test-MyCodexGuardedImage $relative -IncludeManagedProcesses) 'Final guard ignored managed backend'
    Assert (-not (Test-MyCodexGuardedImage $relative)) 'Initial guard bypassed dedicated maintenance'
}

# Exercise the complete production .iss, including its exit code and recovery
# callbacks, with an isolated AppId and no real application/service startup.
function Test-Installer($name, $expectedCode, $development = $false) {
    $output = Join-Path $work $name
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $developmentArguments = @()
    if ($development) { $developmentArguments += '/DDevelopmentAuthenticationHost' }
    & $IsccPath /Q ('/DSourceDir=' + $source) ('/DOutputDir=' + $output) /DMaximumInstallRootLength=180 ('/DAuthenticationInstallTest=AuthTest' + [Guid]::NewGuid().ToString('N')) @developmentArguments (Join-Path $repo 'tools\installer\MyCodex.iss')
    Assert ($LASTEXITCODE -eq 0) 'Installer compilation failed'
    $root = Join-Path $output 'installed'
    if ($name -eq 'repair') {
        New-Item -ItemType Directory -Path (Join-Path $root 'runtime\auth-core') -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $root 'runtime\auth-core\mycodex-auth-host.exe'), 'interrupted update', $utf8)
    }
    $installer = Join-Path $output 'MyCodex_v3.0.0_Setup.exe'
    $process = Start-Process -FilePath $installer -ArgumentList @('/SP-', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="'+$root+'"'), ('/LOG="'+$output+'\setup.log"')) -PassThru -WindowStyle Hidden
    try {
        Assert ($process.WaitForExit(120000)) 'Installer timed out'
        Assert ($process.ExitCode -eq $expectedCode) ("Installer $name returned " + $process.ExitCode)
    } finally { $process.Dispose() }
    $log = Get-Content (Join-Path $output 'setup.log') -Raw
    Assert ($log.Contains('action=Finalize')) 'Installer skipped finalization'
    if ($expectedCode -eq 0) {
        Assert ($log.Contains('Authentication startup registrations: result=0')) 'Installer skipped startup registrations'
        Assert (-not $log.Contains('action=Resume')) 'Successful installation ran cancellation recovery'
    } else { Assert ($log.Contains('action=Resume')) 'Failed installation did not attempt recovery' }
}
Test-Installer 'success' 0
Test-Installer 'repair' 0
[IO.File]::WriteAllText((Join-Path $source 'startup-fail'), '', $utf8)
Test-Installer 'startup-failure' 10
Remove-Item -LiteralPath (Join-Path $source 'startup-fail')
Write-Json (Join-Path $package 'mycodex-auth-host.manifest.json') $oldManifest
Test-Installer 'old-package' 10
Write-Json (Join-Path $package 'mycodex-auth-host.manifest.json') $developmentManifest
Test-Installer 'development-refused' 10
Test-Installer 'development-allowed' 0 $true
Write-Host 'PASS: package identity, cancellation recovery, old resident, native path fallback, managed guard, and production installer success/failure exit codes.'
Write-Host ('Artifacts: ' + $work)
