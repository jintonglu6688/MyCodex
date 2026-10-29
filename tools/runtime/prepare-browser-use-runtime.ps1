param(
    [string]$SourceRuntime,
    [string]$VerifyRoot,
    [switch]$UpdatePin
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$pinPath = Join-Path $PSScriptRoot 'browser-use-bundle.json'
$pin = Get-Content -LiteralPath $pinPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($pin.bundleId -notmatch '^[a-zA-Z0-9._-]+$' -or $pin.bundleId -in @('.', '..')) { throw 'Invalid Browser Use bundle identity.' }
if ($VerifyRoot -and ($UpdatePin -or $SourceRuntime)) { throw 'VerifyRoot is read-only and cannot prepare or update a bundle.' }
$bundleRoot = if ($VerifyRoot) { [IO.Path]::GetFullPath($VerifyRoot) } else { Join-Path $repoRoot ('runtime\browser\' + $pin.bundleId) }
$runtimeRoot = $bundleRoot
$manifestPath = Join-Path $bundleRoot 'bundle.json'
$utf8 = New-Object Text.UTF8Encoding($false)

function Assert-UnlinkedAncestors([string]$path) {
    $current = [IO.Path]::GetFullPath($path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $entry = Get-Item -LiteralPath $current -Force
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked Browser Use ancestor is unsupported.' }
        }
        $parent = [IO.Directory]::GetParent($current)
        $current = if ($parent) { $parent.FullName } else { $null }
    }
}

function Assert-UnlinkedTree([string]$root) {
    $entry = Get-Item -LiteralPath $root -Force
    if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked Browser Use path is unsupported.' }
    if ($entry.PSIsContainer) {
        foreach ($child in Get-ChildItem -LiteralPath $root -Force) { Assert-UnlinkedTree $child.FullName }
    }
}

function Assert-RuntimeMetadata([string]$root) {
    $runtime = Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $browser = Get-Content -LiteralPath (Join-Path $root 'bin\node_modules\@oai\browser-desktop\package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $repl = Get-Content -LiteralPath (Join-Path $root 'bin\node_modules\@oai\cua-repl\package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($runtime.platform -ne 'windows' -or $runtime.arch -ne 'x64' -or $runtime.target -ne 'windows-x64' -or
        $runtime.runtime_archive_version -ne $pin.runtimeVersion -or $runtime.node_version -ne $pin.nodeVersion -or
        $runtime.node_path -ne 'bin/node.exe' -or $runtime.node_repl_path -ne 'bin/node_repl.exe' -or $runtime.node_modules -ne 'bin/node_modules' -or
        $browser.name -ne '@oai/browser-desktop' -or $browser.version -ne $pin.browserVersion -or
        $repl.name -ne '@oai/cua-repl' -or $repl.version -ne $pin.cuaReplVersion) { throw 'Browser Use component metadata does not match the reviewed pin.' }
}

function Assert-OfficialExecutables([string]$root) {
    foreach ($relative in @('bin\node.exe', 'bin\node_repl.exe', 'bin\node_modules\@oai\sky\bin\windows\codex-computer-use.exe')) {
        $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $root $relative)
        if ($signature.Status -ne 'Valid' -or
            $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne 'OpenAI OpCo, LLC') {
            throw 'Official Browser Use executable signature is invalid.'
        }
    }
}

Assert-UnlinkedAncestors $bundleRoot
if (-not (Test-Path -LiteralPath $runtimeRoot)) {
    if ($VerifyRoot) { throw 'The packaged Browser Use bundle is missing.' }
    if ([string]::IsNullOrWhiteSpace($SourceRuntime)) {
        $installedRoot = Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\runtimes\cua_node'
        $candidates = @(if (Test-Path -LiteralPath $installedRoot) {
            foreach ($candidate in Get-ChildItem -LiteralPath $installedRoot -Directory) {
                $metadataPath = Join-Path $candidate.FullName 'manifest.json'
                if (Test-Path -LiteralPath $metadataPath) {
                    try {
                        $metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
                        if ($metadata.runtime_archive_version -eq $pin.runtimeVersion) { $candidate.FullName }
                    } catch { }
                }
            }
        })
        if ($candidates.Count -ne 1) { throw 'No unique pinned official Browser Use runtime. Supply -SourceRuntime from the reviewed official package.' }
        $SourceRuntime = $candidates[0]
    }
    Assert-UnlinkedAncestors $SourceRuntime
    Assert-UnlinkedTree $SourceRuntime
    Assert-RuntimeMetadata $SourceRuntime
    Assert-OfficialExecutables $SourceRuntime
    if (Test-Path -LiteralPath $bundleRoot) { throw 'Incomplete destination exists; preserve it and prepare a fresh reviewed bundle.' }
    New-Item -ItemType Directory -Path $bundleRoot | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $SourceRuntime -Force) { Copy-Item -LiteralPath $entry.FullName -Destination $runtimeRoot -Recurse }
}
Assert-UnlinkedTree $bundleRoot
Assert-RuntimeMetadata $runtimeRoot
if ($VerifyRoot -and -not (Test-Path -LiteralPath $manifestPath)) { throw 'The packaged Browser Use manifest is missing.' }

if ($UpdatePin -or -not (Test-Path -LiteralPath $manifestPath)) {
    Assert-OfficialExecutables $runtimeRoot
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $bundleRoot -File -Recurse) {
        if ($file.FullName -ne $manifestPath) { $paths.Add($file.FullName.Substring($bundleRoot.Length + 1).Replace('\', '/')) }
    }
    $paths.Sort([StringComparer]::Ordinal)
    $entries = @(foreach ($relative in $paths) {
        $path = Join-Path $bundleRoot $relative
        [ordered]@{ path = $relative; size = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    })
    $manifest = [ordered]@{
        schemaVersion = 1; bundleId = $pin.bundleId; runtimeVersion = $pin.runtimeVersion; nodeVersion = $pin.nodeVersion
        browserVersion = $pin.browserVersion; cuaReplVersion = $pin.cuaReplVersion; files = $entries
    }
    $manifestText = ($manifest | ConvertTo-Json -Depth 6).Replace("`r`n", "`n") + "`n"
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $manifestHash = [BitConverter]::ToString($hasher.ComputeHash($utf8.GetBytes($manifestText))).Replace('-', '') }
    finally { $hasher.Dispose() }
    if (-not $UpdatePin -and $manifestHash -ne $pin.manifestSha256) { throw 'Prepared Browser Use files do not match the reviewed bundle pin.' }
    [IO.File]::WriteAllText($manifestPath, $manifestText, $utf8)
    if ($UpdatePin) {
        $pin.manifestSha256 = $manifestHash
        [IO.File]::WriteAllText($pinPath, ($pin | ConvertTo-Json).Replace("`r`n", "`n") + "`n", $utf8)
    }
}

if ($pin.manifestSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or -not (Test-Path -LiteralPath $manifestPath) -or
    (Get-Item -LiteralPath $manifestPath).Length -gt 4MB -or
    (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $pin.manifestSha256) { throw 'Browser Use manifest pin mismatch. Review inputs before -UpdatePin.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) { throw 'Browser Use manifest schema mismatch.' }
foreach ($key in @('bundleId', 'runtimeVersion', 'nodeVersion', 'browserVersion', 'cuaReplVersion')) {
    if ($manifest.$key -ne $pin.$key) { throw 'Browser Use manifest identity mismatch.' }
}
$files = @(Get-ChildItem -LiteralPath $bundleRoot -File -Recurse | Where-Object { $_.FullName -ne $manifestPath })
if ($files.Count -eq 0 -or $files.Count -ne $manifest.files.Count -or $files.Count -gt 10000) { throw 'Browser Use bundle file count mismatch.' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.files) {
    if ($entry.path -match '(^/|\\|:|(^|/)\.{1,2}(/|$)|//)' -or -not $seen.Add($entry.path) -or
        $entry.sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or $entry.size -lt 0) { throw 'Invalid Browser Use manifest entry.' }
    $path = [IO.Path]::GetFullPath((Join-Path $bundleRoot $entry.path))
    if (-not $path.StartsWith($bundleRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Browser Use path escaped root.' }
    if ((Get-Item -LiteralPath $path).Length -ne $entry.size -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw 'Browser Use content hash mismatch.' }
}
foreach ($required in @('bin/node.exe', 'bin/node_repl.exe', 'bin/node_modules/@oai/cua-repl/bin/cua-repl.mjs',
    'bin/node_modules/@oai/browser-desktop/scripts/browser-service.mjs', 'bin/node_modules/@oai/browser-desktop/scripts/browser-client.mjs')) {
    if (-not $seen.Contains($required)) { throw 'Browser Use required entry is missing.' }
}
Write-Host "Prepared and verified Browser Use bundle: $($pin.bundleId)"
