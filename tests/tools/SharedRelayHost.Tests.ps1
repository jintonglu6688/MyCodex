param([Parameter(Mandatory = $true)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$testRoot = Join-Path $repo ('artifacts\shared-relay-probe-' + [Guid]::NewGuid().ToString('N'))
$bin = Join-Path $testRoot 'bin'
$data = Join-Path $testRoot 'data'
$relayDirectory = Join-Path $bin 'runtime\relay'
New-Item -ItemType Directory -Path $relayDirectory, $data -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references = @('Infrastructure', 'Application', 'Domain', 'Shared') | ForEach-Object {
    $source = Join-Path $BuildDirectory ($_ + '.dll')
    Copy-Item -LiteralPath $source -Destination $bin
    '/reference:' + (Join-Path $bin ($_ + '.dll'))
}
$probe = Join-Path $bin 'SharedRelayLifecycleProbe.exe'
& $compiler /nologo /target:exe /platform:x64 ('/out:' + $probe) @references (Join-Path $PSScriptRoot 'SharedRelayLifecycleProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
$fakeSource = Join-Path $testRoot 'FakeRelay.cs'
@'
using System;
using System.Threading;
internal static class FakeRelay
{
    private static void Main()
    {
        while (true)
        {
            try { Console.WriteLine("relay-heartbeat"); Console.Error.WriteLine("relay-diagnostic"); }
            catch (System.IO.IOException) { }
            Thread.Sleep(100);
        }
    }
}
'@ | Set-Content -LiteralPath $fakeSource -Encoding UTF8
$relayExe = Join-Path $relayDirectory 'mycodex-relay.exe'
& $compiler /nologo /target:exe /platform:x64 ('/out:' + $relayExe) $fakeSource
if ($LASTEXITCODE -ne 0) { throw 'Fake Relay compilation failed.' }

function Wait-Until([scriptblock]$Condition, [string]$Message) {
    $deadline = [DateTime]::UtcNow.AddSeconds(35)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 100
    }
    throw $Message
}
function Read-Ownership {
    $path = Join-Path $data 'Relay\shared-host.json'
    if (Test-Path -LiteralPath $path) { Read-JsonSnapshot $path }
}
function Read-Service {
    Read-JsonSnapshot (Join-Path $data 'config\relay-service.json')
}
function Read-JsonSnapshot([string]$Path) {
    # File.Replace can briefly deny even a delete-sharing open on Windows.
    $sharing = [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    while ($true) {
        $stream = $null
        $reader = $null
        try {
            $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, $sharing)
            $reader = [IO.StreamReader]::new($stream)
            return ($reader.ReadToEnd() | ConvertFrom-Json)
        }
        catch {
            $exception = $_.Exception
            while ($null -ne $exception.InnerException) { $exception = $exception.InnerException }
            if (!($exception -is [IO.IOException]) -or [DateTime]::UtcNow -ge $deadline) { throw }
            Start-Sleep -Milliseconds 50
        }
        finally {
            if ($null -ne $reader) { $reader.Dispose() }
            elseif ($null -ne $stream) { $stream.Dispose() }
        }
    }
}
function Stop-Verified($Identity, [string]$ExpectedPath) {
    if ($null -eq $Identity -or $Identity.ProcessId -le 0) { return }
    $process = Get-Process -Id $Identity.ProcessId -ErrorAction SilentlyContinue
    if ($null -ne $process -and $process.Path -eq $ExpectedPath -and
        $process.StartTime.ToUniversalTime().Ticks -eq [long]$Identity.StartTimeUtcTicks) {
        $process.Kill()
        $process.WaitForExit(5000) | Out-Null
    }
}

$clients = @()
try {
    $seed = Start-Process -FilePath $probe -ArgumentList @('seed', ('"' + $data + '"')) -PassThru -WindowStyle Hidden -Wait
    if ($seed.ExitCode -ne 0) { throw 'Seed failed.' }
    foreach ($name in @('creator', 'survivor')) {
        $clients += Start-Process -FilePath $probe -ArgumentList @('client', ('"' + $data + '"'), $name) -PassThru -WindowStyle Hidden
    }
    Wait-Until { (Test-Path (Join-Path $data 'creator.ready')) -and (Test-Path (Join-Path $data 'survivor.ready')) } 'Both clients did not acquire the shared Relay.'
    $initial = Read-Ownership
    if ($initial.Leases.Count -ne 2) { throw 'Expected exactly two leases.' }
    $initialService = Read-Service
    $initialPid = $initialService.EmbeddedRelayProcessId
    $log = Join-Path $data 'Relay\relay.out.log'
    Wait-Until { (Test-Path $log) -and (Get-Item $log).Length -gt 0 } 'Relay log pipe did not produce output.'

    $clients[0].Kill()
    $clients[0].WaitForExit(5000) | Out-Null
    Wait-Until { (Read-Ownership).Leases.Count -eq 1 } 'Crashed creator lease was not reclaimed.'
    if ((Read-Service).EmbeddedRelayProcessId -ne $initialPid) { throw 'Creator exit restarted the shared Relay.' }
    $length = (Get-Item $log).Length
    Wait-Until { (Get-Item $log).Length -gt $length } 'Creator exit broke Relay logging.'

    Stop-Verified $initial.Host $probe
    Wait-Until {
        $state = Read-Ownership
        $state.Ready -and $null -ne $state.Host -and $state.Host.ProcessId -ne $initial.Host.ProcessId
    } 'Survivor did not recover the crashed helper.'
    $recovered = Read-Service
    if ($recovered.EmbeddedRelayProcessId -eq $initialPid) { throw 'Broken-pipe Relay was not restarted.' }
    foreach ($property in @('EncryptedAccessSecret', 'EmbeddedRelayCertificateSha256', 'RelaySigningPublicKey', 'RelaySigningKeyFingerprint')) {
        if ($recovered.$property -ne $initialService.$property) { throw ('Recovery changed ' + $property) }
    }
    $length = (Get-Item $log).Length
    Wait-Until { (Get-Item $log).Length -gt $length } 'Recovered helper did not drain Relay logs.'

    Set-Content -LiteralPath (Join-Path $data 'survivor.exit') -Value 'exit'
    Wait-Until { $null -eq (Read-Ownership).Host -and (Read-Service).EmbeddedRelayProcessId -eq 0 } 'Last user did not stop the shared Relay.'
    Write-Output ('PASS: concurrent acquire, creator crash, independent logging, helper crash recovery, credentials preserved, last-user shutdown. Evidence: ' + $testRoot)
}
finally {
    foreach ($client in $clients) {
        try {
            if (!$client.HasExited) { $client.Kill(); $client.WaitForExit(5000) | Out-Null }
        }
        catch { Write-Warning ('Client cleanup: ' + $_.Exception.Message) }
    }
    try {
        $state = Read-Ownership
        if ($null -ne $state) { Stop-Verified $state.Host $probe }
    }
    catch { Write-Warning ('Helper record cleanup: ' + $_.Exception.Message) }
    try {
        $servicePath = Join-Path $data 'config\relay-service.json'
        if (Test-Path $servicePath) {
            $service = Read-Service
            Stop-Verified ([pscustomobject]@{
                ProcessId = $service.EmbeddedRelayProcessId
                StartTimeUtcTicks = $service.EmbeddedRelayProcessStartTimeUtcTicks
            }) $relayExe
        }
    }
    catch { Write-Warning ('Relay record cleanup: ' + $_.Exception.Message) }

    # A failed observer must not prevent cleanup: these exact executable paths are unique to this run.
    foreach ($testExecutable in @($probe, $relayExe)) {
        $processName = [IO.Path]::GetFileNameWithoutExtension($testExecutable)
        foreach ($candidate in @(Get-Process -Name $processName -ErrorAction SilentlyContinue)) {
            try {
                if ($candidate.Path -eq $testExecutable) {
                    Stop-Verified ([pscustomobject]@{
                        ProcessId = $candidate.Id
                        StartTimeUtcTicks = $candidate.StartTime.ToUniversalTime().Ticks
                    }) $testExecutable
                }
            }
            catch { Write-Warning ('Fixture process cleanup: ' + $_.Exception.Message) }
        }
    }
    $remaining = @(Get-Process -Name 'SharedRelayLifecycleProbe', 'mycodex-relay' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $probe -or $_.Path -eq $relayExe })
    if ($remaining.Count -gt 0) { throw ('Test processes remain under ' + $bin) }
}
