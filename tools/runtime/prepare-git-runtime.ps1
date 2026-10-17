param([string]$DestinationRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'git-runtime.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($pin.version -notmatch '^\d+\.\d+\.\d+\.windows\.\d+$' -or $pin.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Invalid Git runtime pin.' }
if (-not $DestinationRoot) { $DestinationRoot = Join-Path $repoRoot 'runtime\git' }
$destination = [IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\')
if ($destination -eq [IO.Path]::GetPathRoot($destination).TrimEnd('\')) { throw 'A drive root is not a Git runtime destination.' }

function Assert-UnlinkedPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked Git runtime path is unsupported.' }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Get-UnlinkedFiles([string]$Root) {
    foreach ($entry in Get-ChildItem -LiteralPath $Root -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked Git runtime content is unsupported.' }
        if ($entry.PSIsContainer) { Get-UnlinkedFiles $entry.FullName }
        else { $entry }
    }
}

Assert-UnlinkedPath $destination
$cacheRoot = Join-Path $repoRoot 'temp\git-runtime'
if (($repoRoot + '\').StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase) -or
    ($cacheRoot + '\').StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase) -or
    ($destination + '\').StartsWith($cacheRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe Git runtime destination.' }
Assert-UnlinkedPath $cacheRoot
[IO.Directory]::CreateDirectory($cacheRoot) | Out-Null
$version = $pin.version -replace '\.windows\.\d+$', ''
$name = "MinGit-$version-64-bit.zip"
$archivePath = Join-Path $cacheRoot $name
Assert-UnlinkedPath $archivePath
if (-not (Test-Path -LiteralPath $archivePath) -or (Get-FileHash -LiteralPath $archivePath).Hash -ine $pin.sha256) {
    $download = Join-Path $cacheRoot ([Guid]::NewGuid().ToString('N') + '.download')
    try {
        Invoke-WebRequest -Uri "https://github.com/git-for-windows/git/releases/download/v$($pin.version)/$name" -OutFile $download -UseBasicParsing -TimeoutSec 120
        if ((Get-FileHash -LiteralPath $download).Hash -ine $pin.sha256) { throw 'Git runtime archive checksum mismatch.' }
        Move-Item -LiteralPath $download -Destination $archivePath -Force
    } finally {
        if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force }
    }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
$stage = Join-Path $cacheRoot ('stage-' + [Guid]::NewGuid().ToString('N'))
if (-not ([IO.Path]::GetFullPath($stage)).StartsWith([IO.Path]::GetFullPath($cacheRoot).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe Git staging path.' }
try {
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $files = @()
    foreach ($entry in $archive.Entries) {
        $relative = $entry.FullName.TrimEnd('/')
        if ($relative -match '(^/|\\|:|(^|/)\.{1,2}(/|$)|//)' -or
            -not $names.Add($relative) -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Unsafe Git runtime archive entry.' }
        foreach ($segment in $relative.Split('/')) {
            if (-not $segment -or $segment.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or
                $segment -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)') { throw 'Unsafe Git runtime archive entry.' }
        }
        if (-not $entry.FullName.EndsWith('/')) { $files += $entry }
    }
    if (-not $names.Contains('cmd/git.exe') -or -not $names.Contains('LICENSE.txt')) { throw 'Git runtime archive is incomplete.' }
    $valid = Test-Path -LiteralPath $destination -PathType Container
    if ($valid) {
        $valid = @(Get-UnlinkedFiles $destination).Count -eq $files.Count
        foreach ($entry in $files) {
            if (-not $valid) { break }
            $path = Join-Path $destination $entry.FullName
            if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $entry.Length) { $valid = $false; break }
            $stream = $entry.Open()
            $hash = [Security.Cryptography.SHA256]::Create()
            try { $expected = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
            finally { $hash.Dispose(); $stream.Dispose() }
            $valid = (Get-FileHash -LiteralPath $path).Hash -ieq $expected
        }
    }
    if ($valid) { Write-Host "Using verified bundled Git $($pin.version)"; return }
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $stage)
    if (@(Get-UnlinkedFiles $stage).Count -ne $files.Count) { throw 'Git runtime extraction is incomplete.' }
    if (Test-Path -LiteralPath $destination) {
        Get-UnlinkedFiles $destination | Out-Null
        Assert-UnlinkedPath $destination
        # Destination is the fully resolved, explicitly selected runtime directory.
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    Move-Item -LiteralPath $stage -Destination $destination
    Write-Host "Prepared bundled Git $($pin.version)"
} finally {
    $archive.Dispose()
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    if (-not $resolvedStage.StartsWith([IO.Path]::GetFullPath($cacheRoot).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe Git staging cleanup path.' }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}
