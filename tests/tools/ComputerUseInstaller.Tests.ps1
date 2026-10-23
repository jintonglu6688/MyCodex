param([string]$SourceDir, [string]$IsccPath = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe', [switch]$FullBundle)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $SourceDir) { $SourceDir = Join-Path $repoRoot 'Release' }
$SourceDir = [IO.Path]::GetFullPath($SourceDir).TrimEnd('\')
$pin = Get-Content (Join-Path $repoRoot 'tools\runtime\computer-use-bundle.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$project = [xml][IO.File]::ReadAllText((Join-Path $repoRoot 'src\MyCodex.WinForms\MyCodex.WinForms.csproj'))
$asset = @($project.SelectNodes('//None[@Include]') | Where-Object { $_.Include -match 'runtime\\(computer-use|cua)\\' })
if ($asset.Count -ne 1) { throw 'Expected one Computer Use asset mapping.' }
$prefix = $asset[0].Link.Split('%')[0] + $pin.bundleId
$relative = $prefix + '\' + (Get-ChildItem -LiteralPath (Join-Path $SourceDir $prefix) -Filter c.h -File -Recurse |
    Where-Object { $_.FullName.EndsWith('\include\leveldb\c.h') } | Sort-Object { $_.FullName.Length } -Descending | Select-Object -First 1 -ExpandProperty FullName).Substring((Join-Path $SourceDir $prefix).Length + 1)
$source = Join-Path $SourceDir $relative
$artifactRoot = Join-Path $repoRoot 'artifacts'
$work = Join-Path $artifactRoot ('ci-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
$fixture = Join-Path $work 'source'
$staged = Join-Path $fixture $relative
if ($FullBundle) {
    $bundleDestination = Join-Path $fixture $prefix
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($bundleDestination)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $SourceDir $prefix) -Destination $bundleDestination -Recurse
} else {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($staged)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $staged
}

$iss = [IO.File]::ReadAllText((Join-Path $repoRoot 'tools\installer\MyCodex.iss'))
$files = [regex]::Match($iss, '(?m)^Source: "\{#SourceDir\}\\\*";.*$').Value.Trim()
if (-not $files) { throw 'Installer package-copy rule was not found.' }
$guard = ''
$limit = 1000
if ($iss.Contains('function CheckInstallPath(): String;')) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repoRoot 'tools\installer\build-installer.ps1'), [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw 'Installer build script syntax failed.' }
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-MaximumInstallRootLength' }, $true)
    if (-not $function) { throw 'Installer path-budget calculation was not found.' }
    Invoke-Expression $function.Extent.Text
    $limit = Get-MaximumInstallRootLength -PackageSourceDir $SourceDir
    $start = $iss.IndexOf('function CheckInstallPath(): String;', [StringComparison]::Ordinal)
    $end = $iss.IndexOf('function CanLaunchInstalledApplication', $start, [StringComparison]::Ordinal)
    $guard = $iss.Substring($start, $end - $start)
    $check = $iss.IndexOf('Result := CheckInstallPath();')
    if ($check -lt 0 -or $check -gt $iss.IndexOf("ExtractTemporaryFile('Test-MyCodexResourceUsers.ps1')")) { throw 'Path guard must precede all installer maintenance.' }
}
$probe = @"
#define SourceDir "$fixture"
#define MaximumInstallRootLength $limit
[Setup]
AppName=MyCodex isolated Computer Use path test
AppVersion=1
DefaultDirName=$work\install
PrivilegesRequired=lowest
CreateUninstallRegKey=no
Uninstallable=no
DisableProgramGroupPage=yes
OutputDir=$work
OutputBaseFilename=path-test
[CustomMessages]
InstallPathTooLong=INSTALL_PATH_TOO_LONG %1
[Files]
$files
[Code]
$guard
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
"@
$probe += if ($guard) { "`n  Result := CheckInstallPath();`n" } else { "`n  Result := '';`n" }
$probe += "end;`n"
$probePath = Join-Path $work 'path-test.iss'
[IO.File]::WriteAllText($probePath, $probe, [Text.UTF8Encoding]::new($false))
& $IsccPath /Q $probePath
if ($LASTEXITCODE -ne 0) { throw 'Isolated path-test compilation failed.' }

$lengths = @(44, 60)
if ($guard) { $lengths += $limit; $lengths += $limit + 1 }
$cases = @()
foreach ($length in $lengths | Select-Object -Unique) {
    $root = (Join-Path $artifactRoot ('p-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))).PadRight($length, 'x')
    if ($root.Length -ne $length -or -not $root.StartsWith($artifactRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test root.' }
    $log = Join-Path $work ('install-' + $length + '.log')
    $destination = Join-Path $root $relative
    try {
        $installer = Start-Process -FilePath (Join-Path $work 'path-test.exe') -ArgumentList @('/SP-', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="' + $root + '"'), ('/LOG="' + $log + '"')) -WindowStyle Hidden -PassThru -Wait
        if ($guard -and $length -gt $limit) {
            if ($installer.ExitCode -eq 0 -or [IO.File]::Exists($destination) -or -not ([IO.File]::ReadAllText($log).Contains('INSTALL_PATH_TOO_LONG'))) { throw 'Overlong install path was not rejected before copying.' }
        } elseif ($installer.ExitCode -ne 0 -or -not [IO.File]::Exists($destination) -or (Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
            throw "Computer Use installation failed at root length $length; see $log"
        }
        if ($FullBundle -and $length -le $limit) {
            $installedBundle = Join-Path $root $prefix
            $manifestPath = Join-Path $installedBundle 'bundle.json'
            if ((Get-FileHash -LiteralPath $manifestPath).Hash -ne $pin.manifestSha256) { throw 'Installed bundle manifest hash mismatch.' }
            $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if (@(Get-ChildItem -LiteralPath $installedBundle -File -Recurse -Force).Count -ne $manifest.files.Count + 1) { throw 'Installed bundle file count mismatch.' }
            foreach ($entry in $manifest.files) {
                $path = Join-Path $installedBundle $entry.path
                if ((Get-Item -LiteralPath $path).Length -ne $entry.size -or (Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256) { throw 'Installed bundle content mismatch.' }
            }
        }
        $cases += [ordered]@{rootLength=$length;temporaryFileLength=([IO.Path]::GetDirectoryName($destination)).Length + 18;exitCode=$installer.ExitCode}
    } finally {
        if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    }
}
$result = [ordered]@{fullBundle=[bool]$FullBundle;maximumInstallRootLength=$limit;cases=$cases}
[IO.File]::WriteAllText((Join-Path $work 'result.json'), ($result | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
$result | ConvertTo-Json -Depth 5
Write-Output "Computer Use installer path tests passed. Evidence: $work"
