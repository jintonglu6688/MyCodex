param([string]$SourceExecutable, [string]$VerifyRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'browser-extension-host.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($pin.bundleId -notmatch '^h[0-9]+$' -or $pin.pluginVersion -notmatch '^[0-9.]+$' -or
    $pin.fileName -ne 'extension-host.exe' -or $pin.sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or $pin.size -le 0) { throw 'Invalid browser extension host pin.' }
if ($VerifyRoot -and $SourceExecutable) { throw 'VerifyRoot cannot prepare files.' }
$destination = if ($VerifyRoot) { [IO.Path]::GetFullPath($VerifyRoot) } else { Join-Path $repoRoot ('runtime\browser-host\' + $pin.bundleId) }

function Assert-Unlinked([string]$path) {
    $current = [IO.Path]::GetFullPath($path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked browser host path is unsupported.' }
        }
        $parent = [IO.Directory]::GetParent($current)
        $current = if ($parent) { $parent.FullName } else { $null }
    }
}

function Assert-Host([string]$path) {
    Assert-Unlinked $path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-Item -LiteralPath $path).Length -ne $pin.size -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $pin.sha256) { throw 'Browser extension host does not match the reviewed pin.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne 'OpenAI OpCo, LLC') {
        throw 'Browser extension host signature is invalid.'
    }
}

Assert-Unlinked $destination
$hostPath = Join-Path $destination $pin.fileName
if (-not (Test-Path -LiteralPath $hostPath)) {
    if ($VerifyRoot) { throw 'Packaged browser extension host is missing.' }
    if (-not $SourceExecutable) {
        $SourceExecutable = Join-Path $env:USERPROFILE ('.codex\plugins\cache\openai-bundled\chrome\' + $pin.pluginVersion + '\extension-host\windows\x64\extension-host.exe')
    }
    Assert-Host $SourceExecutable
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $SourceExecutable -Destination $hostPath
}
Assert-Host $hostPath
if (@(Get-ChildItem -LiteralPath $destination -Force).Count -ne 1) { throw 'Unexpected file in browser extension host bundle.' }
Write-Host "Prepared and verified browser extension host: $($pin.bundleId)"
