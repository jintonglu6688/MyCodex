$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fixtureRoot = Join-Path $repoRoot ('temp\codex-runtime-test-' + [Guid]::NewGuid().ToString('N'))
$fixtureVersion = '0.999.0'
$triples = @('x86_64-pc-windows-msvc', 'aarch64-pc-windows-msvc')
$global:codexRuntimeFixture = @{ Downloads = 0; LatestQueries = 0; Uploads = @{} }
$badIntegrity = $false
$failArm = $false
$archiveHash = $null
$archiveIntegrity = $null

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

# Execute the real scripts against synthetic packages and a mock registry/GitCode.
function git { param([Parameter(ValueFromRemainingArguments = $true)]$Arguments) $global:LASTEXITCODE = 0 }
function Invoke-RestMethod {
    param([string]$Uri, [string]$Method, $Body, $TimeoutSec, [switch]$UseBasicParsing)
    if ($Uri -like 'https://registry.fixture/*/latest') {
        $global:codexRuntimeFixture.LatestQueries++
        return [pscustomobject]@{ version = $fixtureVersion }
    }
    if ($Uri -like 'https://registry.fixture/*') {
        if ($failArm -and $Uri.EndsWith('win32-arm64')) { throw 'Injected ARM download failure' }
        $suffix = if ($Uri.EndsWith('win32-arm64')) { 'win32-arm64' } else { 'win32-x64' }
        $integrity = if ($badIntegrity) { 'sha512-invalid' } else { $archiveIntegrity }
        return [pscustomobject]@{ dist = [pscustomobject]@{
            tarball = "https://packages.fixture/codex-$fixtureVersion-$suffix.tgz"; integrity = $integrity; shasum = ''
        } }
    }
    if ($Uri -like '*upload_url?*') {
        return [pscustomobject]@{ url = $Uri; headers = [pscustomobject]@{} }
    }
    if ($Method -eq 'Post' -and $Uri.EndsWith('/releases')) {
        Assert-True (-not (Test-Path (Join-Path $fixtureRoot 'runtime\codex\0.1.0'))) 'Old versions must be deleted before publication.'
        return [pscustomobject]@{}
    }
    throw "Unexpected fixture API request: $Uri"
}
function Invoke-WebRequest {
    param([string]$Uri, [string]$OutFile, [string]$InFile, [string]$Method, $Headers, $ContentType, $TimeoutSec, [switch]$UseBasicParsing)
    if ($Uri.StartsWith('https://packages.fixture/')) {
        $global:codexRuntimeFixture.Downloads++
        Copy-Item -LiteralPath (Join-Path $fixtureRoot 'package.tgz') -Destination $OutFile
        return
    }
    if ($Method -eq 'Put') {
        $name = [IO.Path]::GetFileName($InFile)
        $global:codexRuntimeFixture.Uploads[$name] = [pscustomobject]@{ Size = (Get-Item $InFile).Length; Hash = (Get-FileHash $InFile).Hash }
        return
    }
    if ($Method -eq 'Head') {
        $name = ($Uri -split '/')[-2]
        return [pscustomobject]@{ Headers = @{ 'Content-Length' = [string]$global:codexRuntimeFixture.Uploads[$name].Size } }
    }
    throw "Unexpected fixture download: $Uri"
}

try {
    foreach ($relative in @('tools\runtime', 'tools\gitcode', 'updates', 'payload\package')) {
        New-Item -ItemType Directory -Path (Join-Path $fixtureRoot $relative) -Force | Out-Null
    }
    foreach ($relative in @('tools\runtime\prepare-bundled-codex-runtime.ps1', 'tools\gitcode\Publish-GitCodeRuntime.ps1')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $relative) -Destination (Join-Path $fixtureRoot $relative)
    }
    # The distribution publisher is outside this test's boundary; verify it receives the prepared manifest.
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'tools\gitcode\Publish-GitCodeDistributionRepo.ps1') -Encoding ASCII -Value 'param($RepoUrl, $CommitMessage, $GitUsername, $GitPassword) $global:LASTEXITCODE = 0'
    foreach ($triple in $triples) {
        $directory = Join-Path $fixtureRoot "payload\package\vendor\$triple\bin"
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $directory 'codex.exe'), "fixture $triple")
    }
    & tar.exe -czf (Join-Path $fixtureRoot 'package.tgz') -C (Join-Path $fixtureRoot 'payload') package
    Assert-True ($LASTEXITCODE -eq 0) 'Could not create fixture package.'
    $archiveHash = (Get-FileHash (Join-Path $fixtureRoot 'package.tgz')).Hash
    $sha512 = [Security.Cryptography.SHA512]::Create()
    try { $archiveIntegrity = 'sha512-' + [Convert]::ToBase64String($sha512.ComputeHash([IO.File]::ReadAllBytes((Join-Path $fixtureRoot 'package.tgz')))) }
    finally { $sha512.Dispose() }
    $manifestPath = Join-Path $fixtureRoot 'updates\stable.json'
    [IO.File]::WriteAllText($manifestPath, '{"schemaVersion":"1","channel":"stable","generatedAt":"old","app":{"version":"8.0.0"},"runtimes":[{"source":"Other","targetTriple":"unrelated","version":"keep"}]}')
    $prepare = Join-Path $fixtureRoot 'tools\runtime\prepare-bundled-codex-runtime.ps1'
    $publish = Join-Path $fixtureRoot 'tools\gitcode\Publish-GitCodeRuntime.ps1'
    $old = Join-Path $fixtureRoot 'runtime\codex\0.1.0'
    New-Item -ItemType Directory -Path $old -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $old 'sentinel'), 'old runtime')
    $outside = Join-Path $fixtureRoot 'debug\runtime\codex\0.1.0'
    New-Item -ItemType Directory -Path $outside -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $outside 'sentinel'), 'preserve output')

    $badIntegrity = $true
    try {
        & $publish -Token 'fixture' -RegistryBaseUrl 'https://registry.fixture'
        throw 'Corrupt package was accepted.'
    } catch { Assert-True ($_.Exception.Message -like '*integrity check failed*') ('Expected integrity failure: ' + $_.Exception.Message) }
    Assert-True (Test-Path (Join-Path $old 'sentinel')) 'Integrity failure deleted the old runtime.'
    Assert-True ($global:codexRuntimeFixture.Uploads.Count -eq 0) 'Integrity failure published assets.'
    $badIntegrity = $false
    $failArm = $true
    try {
        & $publish -Token 'fixture' -RegistryBaseUrl 'https://registry.fixture' -TargetTriple $triples
        throw 'Missing architecture was accepted.'
    } catch { Assert-True ($_.Exception.Message -like '*Injected ARM*') 'Expected architecture failure.' }
    Assert-True (Test-Path (Join-Path $old 'sentinel')) 'Incomplete multi-architecture preparation deleted old versions.'
    Assert-True ($global:codexRuntimeFixture.Uploads.Count -eq 0) 'Incomplete preparation published assets.'
    $failArm = $false
    $global:codexRuntimeFixture.Downloads = 0
    $global:codexRuntimeFixture.LatestQueries = 0
    & $publish -Token 'fixture' -RegistryBaseUrl 'https://registry.fixture' -TargetTriple ($triples + $triples[0])
    Assert-True ($global:codexRuntimeFixture.Downloads -eq 2 -and $global:codexRuntimeFixture.LatestQueries -eq 1) 'Each architecture must download once, with latest resolved once.'
    Assert-True (-not (Test-Path $old)) 'Old repository runtime was not removed.'
    Assert-True (Test-Path (Join-Path $outside 'sentinel')) 'Build output was modified.'
    foreach ($triple in $triples) {
        Assert-True (Test-Path (Join-Path $fixtureRoot "runtime\codex\$fixtureVersion\$triple\vendor\$triple\bin\codex.exe")) 'New local runtime is missing.'
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-True ($manifest.app.version -eq '8.0.0') 'App manifest changed during runtime publication.'
    Assert-True (@($manifest.runtimes | Where-Object { $_.source -eq 'Other' -and $_.version -eq 'keep' }).Count -eq 1) 'Unrelated runtime manifest changed.'
    foreach ($item in @($manifest.runtimes | Where-Object { $_.source -eq 'Bundled' })) {
        Assert-True ($item.version -eq $fixtureVersion -and $item.asset.sha256 -ieq $archiveHash) 'Manifest does not describe the prepared archive.'
        Assert-True ($global:codexRuntimeFixture.Uploads[$item.asset.fileName].Hash -eq $archiveHash) 'Uploaded bytes differ from the locally extracted archive.'
    }
    Assert-True ($global:codexRuntimeFixture.Uploads.ContainsKey('stable.json')) 'Stable pointer was not uploaded.'

    try {
        & $prepare -Version '../escape' -RegistryBaseUrl 'https://registry.fixture'
        throw 'Unsafe version was accepted.'
    } catch { Assert-True ($_.Exception.Message -like '*Invalid Codex runtime version*') 'Expected unsafe version rejection.' }
    try {
        & $prepare -Version $fixtureVersion -DestinationRoot (Join-Path $fixtureRoot 'debug') -PruneOldVersions
        throw 'Non-repository cleanup was accepted.'
    } catch { Assert-True ($_.Exception.Message -like '*limited to this repository*') 'Expected cleanup boundary rejection.' }
    $linkedOld = Join-Path $fixtureRoot 'runtime\codex\0.2.0'
    New-Item -ItemType Junction -Path $linkedOld -Target $outside | Out-Null
    try {
        try {
            & $prepare -Version $fixtureVersion -RegistryBaseUrl 'https://registry.fixture' -PruneOldVersions
            throw 'Linked cleanup candidate was accepted.'
        } catch { Assert-True ($_.Exception.Message -like '*Linked Codex runtime*') 'Expected linked path rejection.' }
        Assert-True (Test-Path (Join-Path $outside 'sentinel')) 'Linked cleanup touched outside content.'
    } finally { [IO.Directory]::Delete($linkedOld) }
    # Existing build/installer callers still prepare locally without publishing or pruning.
    New-Item -ItemType Directory -Path $old -Force | Out-Null
    $beforeUploads = $global:codexRuntimeFixture.Uploads.Count
    $output = & $prepare -Version $fixtureVersion -RegistryBaseUrl 'https://registry.fixture'
    Assert-True ($null -eq $output -and (Test-Path $old) -and $global:codexRuntimeFixture.Uploads.Count -eq $beforeUploads) 'Local-only preparation contract changed.'
    Write-Host 'Codex runtime preparation and publication checks passed.'
} finally {
    if ([IO.Path]::GetFullPath($fixtureRoot).StartsWith($repoRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
