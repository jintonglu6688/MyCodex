Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $repoRoot 'tools\build\ScriptHelpers.ps1')

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$fixture = Join-Path $repoRoot ('artifacts\script-helpers-' + [Guid]::NewGuid().ToString('N'))
$root = Join-Path $fixture 'root'
$child = Join-Path $root 'child'
$sibling = Join-Path $fixture 'root-sibling'
try {
    [IO.Directory]::CreateDirectory($child) | Out-Null
    [IO.Directory]::CreateDirectory($sibling) | Out-Null

    Assert-True (Test-IsPathUnderRoot -Path $child -Root $root) 'A child must be under its root.'
    Assert-True (Test-IsPathUnderRoot -Path ($child + '\') -Root ($root + '\')) 'Trailing separators must not matter.'
    Assert-True (-not (Test-IsPathUnderRoot -Path $root -Root $root)) 'The root itself must not count as under the root.'
    Assert-True (-not (Test-IsPathUnderRoot -Path ($root + '\') -Root $root)) 'The root with a trailing separator must not count as under the root.'
    Assert-True (-not (Test-IsPathUnderRoot -Path $sibling -Root $root)) 'A sibling sharing the name prefix must not count as under the root.'
    Assert-True (-not (Test-IsPathUnderRoot -Path (Join-Path $root '..\root-sibling') -Root $root)) 'A parent traversal must not escape the root.'

    $refused = $false
    try { Remove-DirectorySafely -Path $root -AllowedRoot $root } catch { $refused = $true }
    Assert-True ($refused -and (Test-Path -LiteralPath $root)) 'Deleting the allowed root itself must be refused.'

    $refused = $false
    try { Remove-DirectorySafely -Path $sibling -AllowedRoot $root } catch { $refused = $true }
    Assert-True ($refused -and (Test-Path -LiteralPath $sibling)) 'Deleting outside the allowed root must be refused.'

    Remove-DirectorySafely -Path $child -AllowedRoot $root
    Assert-True (-not (Test-Path -LiteralPath $child)) 'A child directory must be deleted.'
    Remove-DirectorySafely -Path $child -AllowedRoot $root

    $versionProps = Join-Path $fixture 'Version.props'
    [IO.File]::WriteAllText($versionProps, '<Project><PropertyGroup><Version> 9.8.7 </Version></PropertyGroup></Project>')
    Assert-True ((Get-AppVersion -Path $versionProps) -eq '9.8.7') 'Get-AppVersion must read the trimmed version.'

    Write-Host 'Script helper checks passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
