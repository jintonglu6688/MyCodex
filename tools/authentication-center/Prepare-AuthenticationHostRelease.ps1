[CmdletBinding()]
param([string]$DestinationRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) { $DestinationRoot = Join-Path $repoRoot 'runtime\auth-core' }
$destination = [IO.Path]::GetFullPath($DestinationRoot)
$tag = 'auth-core-v0.3.11'
$name = 'mycodex-auth-host-0.3.11-x86_64-pc-windows-msvc.zip'
$archiveSha256 = '3601bb2a3ba5fd965d665e39074d4d2e5e0a3457db714a16b984544ddb97fceb'
$executableSha256 = '4c0d81eca282d84b63a8af0c53b13c0d5f7a674a27fdf27bc604734fc9f581f8'
$sourceRevision = 'db8aaeb16afd633e6fc2a880a7832ef8c376154f'
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
