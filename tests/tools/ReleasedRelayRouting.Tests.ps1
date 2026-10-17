param([Parameter(Mandatory = $true)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$tag = [IO.File]::ReadAllText((Join-Path $repo 'tools\runtime\relay-runtime.version')).Trim()
if ($tag -notmatch '^relay-v\d+\.\d+\.\d+$') { throw 'Invalid pinned Relay version.' }
$expectedHash = [IO.File]::ReadAllText((Join-Path $repo 'tools\runtime\relay-runtime.sha256')).Trim()
$released = Join-Path $repo ('runtime\relay\' + $tag + '\windows-x64\mycodex-relay.exe')
if (!(Test-Path -LiteralPath $released -PathType Leaf)) { throw ('Pinned released Relay is missing: ' + $tag) }
if ((Get-FileHash -LiteralPath $released -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Pinned released Relay hash mismatch.' }
$testRoot = Join-Path $repo ('artifacts\released-relay-routing-' + [Guid]::NewGuid().ToString('N'))
$bin = Join-Path $testRoot 'bin'
$data = Join-Path $testRoot 'private-service'
New-Item -ItemType Directory -Path $bin, $data -Force | Out-Null
$relay = Join-Path $bin 'mycodex-relay.exe'
Copy-Item -LiteralPath $released -Destination $relay
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references = @('Infrastructure', 'Application', 'Domain', 'Shared') | ForEach-Object {
    $source = Join-Path ([IO.Path]::GetFullPath($BuildDirectory)) ($_ + '.dll')
    Copy-Item -LiteralPath $source -Destination $bin
    '/reference:' + (Join-Path $bin ($_ + '.dll'))
}
$probe = Join-Path $bin 'MyCodex.Application.Tests.exe'
& $compiler /nologo /target:exe /platform:x64 ('/out:' + $probe) @references /reference:System.Web.Extensions.dll /reference:System.Runtime.Serialization.dll /reference:System.Net.Http.dll (Join-Path $PSScriptRoot 'ReleasedRelayRoutingProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Released Relay probe compilation failed.' }
$exitCode = 1
try {
    Write-Output ('Pinned Relay: ' + $tag + '; SHA256=' + $expectedHash)
    & $probe $relay $data
    $exitCode = $LASTEXITCODE
}
finally {
    # Only processes whose executable path is unique to this fixture can be stopped.
    foreach ($name in @('MyCodex.Application.Tests', 'mycodex-relay')) {
        foreach ($candidate in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
            if ($candidate.Path -eq $probe -or $candidate.Path -eq $relay) {
                $startTicks = $candidate.StartTime.ToUniversalTime().Ticks
                $verified = Get-Process -Id $candidate.Id -ErrorAction SilentlyContinue
                if ($null -ne $verified -and $verified.Path -eq $candidate.Path -and
                    $verified.StartTime.ToUniversalTime().Ticks -eq $startTicks) {
                    $verified.Kill()
                    $verified.WaitForExit(5000) | Out-Null
                }
            }
        }
    }
    $remaining = @(Get-Process -Name 'MyCodex.Application.Tests', 'mycodex-relay' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $probe -or $_.Path -eq $relay })
    if ($remaining.Count -gt 0) { throw 'Probe-owned process remained alive.' }
    # Service DB/config contain isolated tenant credentials. Preserve only safe result evidence.
    $resultPath = Join-Path $data 'result.txt'
    if (Test-Path -LiteralPath $resultPath) { Copy-Item -LiteralPath $resultPath -Destination (Join-Path $testRoot 'result.txt') }
    $allowed = [IO.Path]::GetFullPath($testRoot).TrimEnd('\') + '\'
    $resolvedData = [IO.Path]::GetFullPath($data)
    if (!$resolvedData.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe probe cleanup path.' }
    Remove-Item -LiteralPath $resolvedData -Recurse -Force
    Write-Output ('Safe evidence: ' + $testRoot)
    Write-Output 'Manual acceptance remains: native Android, existing paired phone upgrade, public TLS/network and keyboard/TalkBack.'
}
if ($exitCode -ne 0) { throw 'Pinned released Relay routing probe failed; inspect result.txt for its safe failure stage.' }
