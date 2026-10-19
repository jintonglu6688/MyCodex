function Test-AuthenticationHostPackage {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)][string]$PackageRoot, [switch]$AllowDevelopment)

    $executable = Join-Path $PackageRoot 'mycodex-auth-host.exe'
    $manifestPath = Join-Path $PackageRoot 'mycodex-auth-host.manifest.json'
    foreach ($path in @($executable, $manifestPath, (Join-Path $PackageRoot 'LICENSE'))) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw 'Authentication host package is incomplete. Prepare a pinned package before packaging MyCodex.'
        }
    }
    if ((Get-Item -LiteralPath $manifestPath).Length -gt 65536) { throw 'Authentication host manifest is too large.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.executable -cne 'mycodex-auth-host.exe' -or
        $manifest.hostVersion -cne '0.3.9' -or $manifest.protocolVersion -ne 3 -or
        $manifest.upstreamRevision -cne '790ed8009df809bedaf6c31b5ced05da46bb1f1f' -or
        $manifest.target -cne 'x86_64-pc-windows-msvc' -or $manifest.sourceDirty -isnot [bool] -or
        $manifest.sourceRevision -cnotmatch '^[0-9a-f]{40}$' -or $manifest.sha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw 'Authentication host package is incompatible or has unknown provenance.'
    }
    if ($manifest.sourceDirty -and -not $AllowDevelopment) { throw 'Release packaging requires clean authentication host source.' }
    if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ine $manifest.sha256) { throw 'Authentication host executable checksum mismatch.' }
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = [IO.Path]::GetFullPath($executable)
    $start.Arguments = '--version-json'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(10000)) { $process.Kill(); throw 'Authentication host identity query timed out.' }
        if ($process.ExitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($errorOutput.Result)) { throw 'Authentication host identity query failed.' }
        $identity = $output.Result | ConvertFrom-Json
        foreach ($field in @('hostVersion','protocolVersion','upstreamRevision','sourceRevision','sourceDirty','target','sha256')) {
            if ($identity.$field -cne $manifest.$field) { throw 'Authentication host identity does not match its manifest.' }
        }
    } finally { $process.Dispose() }
    return $manifest
}

function Test-AuthenticationInstallerReceipt {
    param([Parameter(Mandatory=$true)][string]$InstallerPath)
    $receiptPath = $InstallerPath + '.auth.json'
    if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw 'Installer authentication package receipt is missing. Rebuild the installer.' }
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($receipt.schemaVersion -ne 1 -or $receipt.development -isnot [bool] -or $receipt.development -or
        $receipt.authenticationHost.sourceDirty -isnot [bool] -or $receipt.authenticationHost.sourceDirty -or
        $receipt.authenticationHost.hostVersion -cne '0.3.9' -or $receipt.authenticationHost.protocolVersion -ne 3 -or
        $receipt.authenticationHost.target -cne 'x86_64-pc-windows-msvc' -or
        $receipt.authenticationHost.upstreamRevision -cne '790ed8009df809bedaf6c31b5ced05da46bb1f1f' -or
        $receipt.authenticationHost.sourceRevision -cnotmatch '^[0-9a-f]{40}$' -or
        $receipt.authenticationHost.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
        $receipt.installerSha256 -ine (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash) {
        throw 'Installer authentication receipt is incompatible, development-only, or does not match the installer.'
    }
}
