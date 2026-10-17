Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fixture = Join-Path $repoRoot ('artifacts\git-prepare-' + [Guid]::NewGuid().ToString('N'))
$scripts = Join-Path $fixture 'tools\runtime'
$destination = Join-Path $fixture 'output\git'
$payload = Join-Path $fixture 'payload'
$sourceArchive = Join-Path $fixture 'release.zip'
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.Directory]::CreateDirectory($scripts) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $payload 'cmd')) | Out-Null
[IO.File]::WriteAllText((Join-Path $payload 'cmd\git.exe'), 'git fixture')
[IO.File]::WriteAllText((Join-Path $payload 'LICENSE.txt'), 'license fixture')
Copy-Item -LiteralPath (Join-Path $repoRoot 'tools\runtime\prepare-git-runtime.ps1') -Destination $scripts
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::Open($sourceArchive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $payload -File -Recurse) {
        $entry = $archive.CreateEntry($file.FullName.Substring($payload.Length + 1).Replace('\', '/'))
        $inputStream = [IO.File]::OpenRead($file.FullName)
        $outputStream = $entry.Open()
        try { $inputStream.CopyTo($outputStream) } finally { $inputStream.Dispose(); $outputStream.Dispose() }
    }
} finally { $archive.Dispose() }
$global:gitFixtureDownloads = 0
function Invoke-WebRequest {
    param([string]$Uri, [string]$OutFile, [switch]$UseBasicParsing, [int]$TimeoutSec)
    if ($Uri -cne 'https://github.com/git-for-windows/git/releases/download/v1.0.0.windows.1/MinGit-1.0.0-64-bit.zip') { throw 'Unexpected Git download source.' }
    $global:gitFixtureDownloads++
    Copy-Item -LiteralPath $sourceArchive -Destination $OutFile
}
function Set-FixturePin([string]$Hash) {
    [IO.File]::WriteAllText((Join-Path $scripts 'git-runtime.json'), (@{version='1.0.0.windows.1';sha256=$Hash} | ConvertTo-Json), $utf8)
}
try {
    Set-FixturePin (Get-FileHash -LiteralPath $sourceArchive).Hash
    & (Join-Path $scripts 'prepare-git-runtime.ps1') -DestinationRoot $destination
    & (Join-Path $scripts 'prepare-git-runtime.ps1') -DestinationRoot $destination
    if ($global:gitFixtureDownloads -ne 1 -or [IO.File]::ReadAllText((Join-Path $destination 'LICENSE.txt')) -ne 'license fixture') { throw 'Verified Git was not completely installed and reused.' }
    [IO.File]::WriteAllText((Join-Path $destination 'cmd\git.exe'), 'tampered')
    & (Join-Path $scripts 'prepare-git-runtime.ps1') -DestinationRoot $destination
    if ($global:gitFixtureDownloads -ne 1 -or [IO.File]::ReadAllText((Join-Path $destination 'cmd\git.exe')) -ne 'git fixture') { throw 'Damaged Git was not repaired from the verified archive.' }
    Set-FixturePin ('0' * 64)
    try {
        & (Join-Path $scripts 'prepare-git-runtime.ps1') -DestinationRoot $destination
        throw 'Unpinned Git archive was accepted.'
    } catch { if ($_.Exception.Message -cne 'Git runtime archive checksum mismatch.') { throw } }
    if ([IO.File]::ReadAllText((Join-Path $destination 'cmd\git.exe')) -ne 'git fixture') { throw 'Failed Git verification modified the old runtime.' }
    $archive = [IO.Compression.ZipFile]::Open($sourceArchive, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.CreateEntry('../escape.txt')
        $writer = [IO.StreamWriter]::new($entry.Open())
        try { $writer.Write('unsafe') } finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
    Set-FixturePin (Get-FileHash -LiteralPath $sourceArchive).Hash
    try {
        & (Join-Path $scripts 'prepare-git-runtime.ps1') -DestinationRoot $destination
        throw 'Traversing Git archive was accepted.'
    } catch { if ($_.Exception.Message -cne 'Unsafe Git runtime archive entry.') { throw } }
    if (Test-Path -LiteralPath (Join-Path $fixture 'temp\git-runtime\escape.txt')) { throw 'Unsafe archive escaped staging.' }
    Write-Output 'Git runtime preparation: full copy, cache reuse, repair, checksum rejection and traversal rejection passed without network.'
} finally {
    $full = [IO.Path]::GetFullPath($fixture)
    if (-not $full.StartsWith((Join-Path $repoRoot 'artifacts') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup.' }
    Remove-Item -LiteralPath $full -Recurse -Force
}
