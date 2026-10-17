[CmdletBinding()]
param([string]$DestinationRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) { $DestinationRoot = Join-Path $repoRoot 'runtime\auth-core' }
$destination = [IO.Path]::GetFullPath($DestinationRoot)
$tag = 'auth-core-v0.3.8'
$name = 'mycodex-auth-host-0.3.8-x86_64-pc-windows-msvc.zip'
$archiveSha256 = 'ac5c6163871f6d0994349b90dd3a4c35ea824b7c4c081121f8df91154808db47'
$executableSha256 = 'ba327e781387c0a459c99cfd97d2e5bd9fd0687763f3e4acc74c0bd2fec49c38'
$sourceRevision = 'bf6eb7cf0468d83a5da1094eea08e8b39f7dbeac'
. (Join-Path $PSScriptRoot 'Test-AuthenticationHostPackage.ps1')

if (Test-Path -LiteralPath (Join-Path $destination 'mycodex-auth-host.manifest.json') -PathType Leaf) {
    try {
        $existing = Test-AuthenticationHostPackage -PackageRoot $destination
        if ($existing.sha256 -ceq $executableSha256 -and $existing.sourceRevision -ceq $sourceRevision) {
            Write-Host "Using verified authentication core in runtime: $tag"
            return
        }
    } catch {
        # An invalid local package is replaced only after a fixed release verifies.
    }
}

$cacheRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "temp\auth-core-release\$tag"))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'temp\auth-core-release')).TrimEnd('\') + '\'
if (-not $cacheRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unsafe authentication core cache path.'
}
[IO.Directory]::CreateDirectory($cacheRoot) | Out-Null
$archivePath = Join-Path $cacheRoot $name
if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf) -or
    (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ine $archiveSha256) {
    $temporary = Join-Path $cacheRoot ($name + '.' + [guid]::NewGuid().ToString('N') + '.download')
    try {
        $url = "https://github.com/jintonglu6688/MyCodex-auth-host/releases/download/$tag/$name"
        Invoke-WebRequest -Uri $url -OutFile $temporary -UseBasicParsing -TimeoutSec 120
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ine $archiveSha256) {
            throw 'Authentication core GitHub archive differs from the pinned release.'
        }
        Move-Item -LiteralPath $temporary -Destination $archivePath -Force
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

$stage = Join-Path $cacheRoot ('stage-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $expected = @('mycodex-auth-host.exe', 'mycodex-auth-host.manifest.json', 'LICENSE', 'RUNTIME.md', 'dependencies.txt')
        if ($archive.Entries.Count -ne $expected.Count -or
            @($archive.Entries | Where-Object { $_.FullName -cnotin $expected }).Count -ne 0) {
            throw 'Authentication core GitHub archive has unexpected entries.'
        }
        foreach ($fileName in @('mycodex-auth-host.exe', 'mycodex-auth-host.manifest.json', 'LICENSE')) {
            $entry = $archive.GetEntry($fileName)
            if ($null -eq $entry) { throw "Authentication core archive is missing $fileName" }
            $inputStream = $entry.Open()
            $outputStream = [IO.File]::Create((Join-Path $stage $fileName))
            try { $inputStream.CopyTo($outputStream) }
            finally { $outputStream.Dispose(); $inputStream.Dispose() }
        }
    } finally { $archive.Dispose() }
    $package = Test-AuthenticationHostPackage -PackageRoot $stage
    if ($package.sha256 -cne $executableSha256 -or $package.sourceRevision -cne $sourceRevision) {
        throw 'Authentication core Windows identity differs from the pinned release.'
    }
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    foreach ($fileName in @('mycodex-auth-host.exe', 'mycodex-auth-host.manifest.json', 'LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $stage $fileName) -Destination (Join-Path $destination $fileName) -Force
    }
    Test-AuthenticationHostPackage -PackageRoot $destination | Out-Null
    Write-Host "Prepared authentication core $tag from the fixed GitHub release."
} finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    if ($resolvedStage.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
