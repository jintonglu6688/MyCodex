param(
    [string]$SourceResources,
    [switch]$UpdatePin
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$pinPath = Join-Path $PSScriptRoot 'computer-use-bundle.json'
$pin = Get-Content -LiteralPath $pinPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($pin.bundleId -notmatch '^[a-zA-Z0-9._-]+$') { throw 'Invalid Computer Use bundle identity.' }
$bundleRoot = Join-Path $repoRoot ('runtime\cua\' + $pin.bundleId)
$runtimeRoot = Join-Path $bundleRoot 'cua_node'
$pluginRoot = Join-Path $bundleRoot 'marketplace\plugins\computer-use'
$manifestPath = Join-Path $bundleRoot 'bundle.json'
$utf8 = New-Object Text.UTF8Encoding($false)

function Assert-UnlinkedTree([string]$root) {
    $entry = Get-Item -LiteralPath $root -Force
    if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked Computer Use path is unsupported.' }
    if ($entry.PSIsContainer) {
        foreach ($child in Get-ChildItem -LiteralPath $root -Force) {
            Assert-UnlinkedTree $child.FullName
        }
    }
}

if (-not (Test-Path -LiteralPath $runtimeRoot) -or -not (Test-Path -LiteralPath $pluginRoot)) {
    if ([string]::IsNullOrWhiteSpace($SourceResources)) {
        $package = Get-AppxPackage -Name OpenAI.Codex |
            Where-Object { $_.PackageFamilyName -eq 'OpenAI.Codex_2p2nqsd0c76g0' } |
            Select-Object -First 1
        if ($null -eq $package) { throw 'No prepared Computer Use bundle. Supply -SourceResources from the official client package.' }
        $SourceResources = Join-Path $package.InstallLocation 'app\resources'
    }
    $runtimeSource = Join-Path $SourceResources 'cua_node'
    $pluginSource = Join-Path $SourceResources 'plugins\openai-bundled\plugins\computer-use'
    Assert-UnlinkedTree $runtimeSource
    Assert-UnlinkedTree $pluginSource
    if (Test-Path -LiteralPath $bundleRoot) { throw 'Incomplete destination exists; preserve it and prepare into a fresh bundle directory.' }
    New-Item -ItemType Directory -Path (Join-Path $bundleRoot 'marketplace\plugins') -Force | Out-Null
    Copy-Item -LiteralPath $runtimeSource -Destination $runtimeRoot -Recurse
    Copy-Item -LiteralPath $pluginSource -Destination $pluginRoot -Recurse
}
Assert-UnlinkedTree $bundleRoot

if (-not $UpdatePin) {
    if ($pin.manifestSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or -not (Test-Path -LiteralPath $manifestPath)) { throw 'Bundle pin is missing. Review the inputs and run once with -UpdatePin.' }
    if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $pin.manifestSha256) { throw 'Computer Use manifest hash mismatch.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $files = @(Get-ChildItem -LiteralPath $bundleRoot -File -Recurse | Where-Object { $_.FullName -ne $manifestPath })
    if ($files.Count -ne $manifest.files.Count) { throw 'Computer Use bundle file count mismatch.' }
    foreach ($entry in $manifest.files) {
        if ($entry.path -match '(^/|\\|:|(^|/)\.{1,2}(/|$))') { throw 'Unsafe bundle manifest path.' }
        $path = [IO.Path]::GetFullPath((Join-Path $bundleRoot $entry.path))
        if (-not $path.StartsWith([IO.Path]::GetFullPath($bundleRoot).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Bundle path escaped root.' }
        if ((Get-Item -LiteralPath $path).Length -ne $entry.size -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw 'Computer Use content hash mismatch.' }
    }
} else {
    $runtime = Get-Content -LiteralPath (Join-Path $runtimeRoot 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $plugin = Get-Content -LiteralPath (Join-Path $pluginRoot '.codex-plugin\plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($runtime.target -ne 'windows-x64' -or $runtime.runtime_archive_version -ne $pin.runtimeVersion -or $plugin.name -ne 'computer-use' -or $plugin.version -ne $pin.pluginVersion) { throw 'Source component versions do not match the reviewed pin.' }
    foreach ($relative in @('bin\node.exe','bin\node_repl.exe','bin\node_modules\@oai\sky\bin\windows\codex-computer-use.exe')) {
        $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $runtimeRoot $relative)
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false) -ne 'OpenAI OpCo, LLC') { throw 'Official component signature is invalid.' }
    }
    $marketPath = Join-Path $bundleRoot 'marketplace\.agents\plugins\marketplace.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $marketPath) -Force | Out-Null
    $catalog = [ordered]@{
        name = 'mycodex-computer-use'
        plugins = @([ordered]@{
            name = 'computer-use'
            source = [ordered]@{source='local';path='./plugins/computer-use'}
            policy = [ordered]@{installation='AVAILABLE';authentication='ON_INSTALL'}
            category = 'Productivity'
        })
    }
    [IO.File]::WriteAllText($marketPath, ($catalog | ConvertTo-Json -Depth 8).Replace("`r`n","`n") + "`n", $utf8)
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $bundleRoot -File -Recurse) {
        if ($file.FullName -ne $manifestPath) { $paths.Add($file.FullName.Substring($bundleRoot.Length + 1).Replace('\','/')) }
    }
    $paths.Sort([StringComparer]::Ordinal)
    $entries = @(foreach ($relative in $paths) {
        $path = Join-Path $bundleRoot $relative
        [ordered]@{path=$relative;size=(Get-Item -LiteralPath $path).Length;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
    })
    $manifest = [ordered]@{schemaVersion=1;bundleId=$pin.bundleId;runtimeVersion=$pin.runtimeVersion;pluginVersion=$pin.pluginVersion;files=$entries}
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6).Replace("`r`n","`n") + "`n", $utf8)
    $pin.manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    [IO.File]::WriteAllText($pinPath, ($pin | ConvertTo-Json).Replace("`r`n","`n") + "`n", $utf8)
}
Write-Host "Prepared and verified Computer Use bundle: $($pin.bundleId)"
