param(
    [Parameter(Mandatory = $true)][ValidateSet('Prepare', 'Finalize', 'Resume', 'Uninstall')][string]$Action,
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true)][string]$StatePath,
    [string]$ExpectedManifestPath,
    [string]$VerificationScript,
    [switch]$AllowDevelopmentAuthenticationHost
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
$root = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$executable = Join-Path $root 'runtime\auth-core\mycodex-auth-host.exe'
$packageDirectory = Split-Path -Parent $executable
$stateFile = [IO.Path]::GetFullPath($StatePath)
$serializer = [Text.UTF8Encoding]::new($false)
if (-not $VerificationScript) { $VerificationScript = Join-Path $PSScriptRoot '..\authentication-center\Test-AuthenticationHostPackage.ps1' }

function Write-Diagnostic([string]$message) {
    # Only fixed codes, process IDs and validated package identities; never RPC payloads.
    try { [IO.File]::AppendAllText(($stateFile + '.log'), ([DateTime]::UtcNow.ToString('o') + ' ' + $Action + ' ' + $message + "`r`n"), $serializer) } catch { }
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AuthenticationHostArguments {
    [DllImport("shell32.dll", SetLastError=true)] static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
    public static string[] Parse(string commandLine) {
        int count;
        var pointer = CommandLineToArgvW(commandLine, out count);
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("invalid_command_line");
        try {
            var result = new string[count];
            for (int i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size));
            return result;
        } finally { LocalFree(pointer); }
    }
    public static string Quote(string value) {
        var result = new System.Text.StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value) {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
'@

function New-HostStartInfo($target, [string]$command) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $executable
    $info.Arguments = $command + ' --data-dir ' + [AuthenticationHostArguments]::Quote($target.store) + ' --codex-home ' + [AuthenticationHostArguments]::Quote($target.home)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    return $info
}

function Invoke-HostRequest($target, [string]$method, $parameters) {
    $process = New-Object Diagnostics.Process
    $process.StartInfo = New-HostStartInfo $target 'rpc'
    try {
        $process.Start() | Out-Null
        $request = @{jsonrpc='2.0'; id=1; method=$method; params=$parameters} | ConvertTo-Json -Compress
        $bytes = $serializer.GetBytes($request)
        $process.StandardInput.BaseStream.Write($bytes,0,$bytes.Length)
        $process.StandardInput.BaseStream.Close()
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) { throw 'authentication_update_timeout' }
        if ($process.ExitCode -ne 0) { throw 'authentication_backend_unavailable' }
        $reply = $output.GetAwaiter().GetResult() | ConvertFrom-Json
        if ($reply.PSObject.Properties['error']) { throw 'authentication_backend_busy_or_route_required' }
        return $reply.result
    } finally { $process.Dispose() }
}

function Start-Host($target) {
    $process = New-Object Diagnostics.Process
    $process.StartInfo = New-HostStartInfo $target 'serve'
    try {
        $process.Start() | Out-Null
        $process.StandardInput.Close()
        $ready = $process.StandardOutput.ReadLineAsync()
        if (-not $ready.Wait(15000)) { throw 'authentication_restart_timeout' }
        $line = $ready.Result
        if ($null -eq $line) {
            $failure = $process.StandardError.ReadToEnd().Trim()
            if ($failure -cne 'backend_already_running') { throw 'authentication_restart_failed' }
        } elseif (($line | ConvertFrom-Json).event -ne 'ready') { throw 'authentication_restart_failed' }
        $status = Invoke-HostRequest $target 'status' @{}
        Assert-HostIdentity $status $target
        if ($status.route.takeover -and (-not $status.route.running -or -not $status.route.accepting)) { throw 'authentication_route_not_ready' }
    } finally { $process.Dispose() }
}

function Normalize-TargetPath([string]$path) {
    if ($path.StartsWith('\\?\UNC\', [StringComparison]::OrdinalIgnoreCase)) { $path = '\\' + $path.Substring(8) }
    elseif ($path.StartsWith('\\?\', [StringComparison]::OrdinalIgnoreCase)) { $path = $path.Substring(4) }
    return [IO.Path]::GetFullPath($path).TrimEnd('\')
}

function Assert-HostIdentity($status, $target) {
    $manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $packageDirectory 'mycodex-auth-host.manifest.json') | ConvertFrom-Json
    if ([string]$status.hostVersion -cmatch '^[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,4}$') {
        Write-Diagnostic ('resident_version=' + $status.hostVersion)
    }
    foreach ($field in @('hostVersion','protocolVersion','upstreamRevision','sourceRevision','sourceDirty','target','sha256')) {
        if ($status.$field -cne $manifest.$field) {
            Write-Diagnostic ('identity_mismatch field=' + $field)
            throw 'authentication_update_identity_mismatch'
        }
    }
    if ((Normalize-TargetPath $status.executablePath) -ine (Normalize-TargetPath $executable)) { throw 'authentication_update_installation_mismatch' }
    if ((Normalize-TargetPath $status.codexHome) -ine (Normalize-TargetPath $target.home) -or
        (Normalize-TargetPath $status.dataDir) -ine (Normalize-TargetPath $target.store)) { throw 'authentication_update_target_mismatch' }
}

function Write-State($state) {
    $temporary = $stateFile + '.' + [Guid]::NewGuid().ToString('N')
    try {
        [IO.File]::WriteAllText($temporary, ($state | ConvertTo-Json -Depth 4), $serializer)
        if (Test-Path -LiteralPath $stateFile) { [IO.File]::Replace($temporary,$stateFile,[NullString]::Value) }
        else { [IO.File]::Move($temporary,$stateFile) }
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}

function Test-PackageHash([string]$directory) {
    try {
        $manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $directory 'mycodex-auth-host.manifest.json') | ConvertFrom-Json
        # Maintenance accepts the installed build's own identity, so a later release
        # can stop and recover an earlier core package (protocol 2 before 0.3.0).
        return $manifest.schemaVersion -eq 1 -and $manifest.protocolVersion -in @(2, 3) -and
            $manifest.sourceRevision -cmatch '^[0-9a-f]{40}$' -and $manifest.sourceDirty -is [bool] -and
            $manifest.target -ceq 'x86_64-pc-windows-msvc' -and
            $manifest.executable -ceq 'mycodex-auth-host.exe' -and $manifest.sha256 -match '^[a-fA-F0-9]{64}$' -and
            (Test-Path -LiteralPath (Join-Path $directory 'LICENSE')) -and
            (Get-FileHash -LiteralPath (Join-Path $directory 'mycodex-auth-host.exe') -Algorithm SHA256).Hash -ieq $manifest.sha256
    } catch { return $false }
}

try {
    Write-Diagnostic 'started'
    if ($Action -in @('Resume', 'Finalize')) {
        if ($Action -eq 'Finalize') {
            # The expected manifest is extracted from this installer, not read from
            # the destination or a previous update's backup.
            if (-not $ExpectedManifestPath) { throw 'authentication_expected_package_missing' }
            $expected = Get-Content -LiteralPath $ExpectedManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $installed = Get-Content -LiteralPath (Join-Path $packageDirectory 'mycodex-auth-host.manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($field in @('hostVersion','protocolVersion','upstreamRevision','sourceRevision','sourceDirty','target','sha256')) {
                if ($installed.$field -cne $expected.$field) { throw 'authentication_installed_package_mismatch' }
            }
            # Check against the trusted payload before executing --version-json.
            if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ine $expected.sha256) { throw 'authentication_installed_package_mismatch' }
            . $VerificationScript
            try { $installed = Test-AuthenticationHostPackage -PackageRoot $packageDirectory -AllowDevelopment:$AllowDevelopmentAuthenticationHost }
            catch { throw 'authentication_installed_package_invalid' }
            Write-Diagnostic ('verified version=' + $installed.hostVersion + ' sha256=' + $installed.sha256)
        }
        if (-not (Test-Path -LiteralPath $stateFile)) {
            if ($Action -eq 'Finalize') { throw 'authentication_update_state_missing' }
            exit 0
        }
        $state = Get-Content -Raw -Encoding UTF8 $stateFile | ConvertFrom-Json
        if ($state.installRoot -ine $root) { throw 'authentication_update_target_mismatch' }
        if (-not (Test-PackageHash $packageDirectory)) {
            if ($Action -eq 'Finalize') { throw 'authentication_installed_package_invalid' }
            if (-not $state.hadPackage -and @($state.targets).Count -eq 0) { exit 0 }
            $backup = $stateFile + '.backup'
            if (-not (Test-PackageHash $backup)) { throw 'authentication_update_recovery_required' }
            [IO.Directory]::CreateDirectory($packageDirectory) | Out-Null
            foreach ($name in @('mycodex-auth-host.exe','mycodex-auth-host.manifest.json','LICENSE')) {
                Copy-Item -LiteralPath (Join-Path $backup $name) -Destination (Join-Path $packageDirectory $name) -Force
            }
            Write-Diagnostic 'previous_package_restored'
        }
        $failed = $false
        foreach ($target in $state.targets) {
            try { Start-Host $target } catch {
                $failed = $true
                $reason = if ($_.Exception.Message -cmatch '^authentication_[a-z_]+$') { $_.Exception.Message } else { 'authentication_restart_failed' }
                Write-Diagnostic $reason
            }
        }
        if ($failed) { throw 'authentication_update_recovery_required' }
        $state.completed = $true
        Write-State $state
        Write-Diagnostic 'completed'
        exit 0
    }

    $targets = @()
    . (Join-Path $PSScriptRoot 'Test-MyCodexResourceUsers.ps1') -InstallRoot $root -FunctionsOnly
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    foreach ($candidate in @(Get-CimInstance Win32_Process -Filter "Name = 'mycodex-auth-host.exe'")) {
        $path = [string]$candidate.ExecutablePath
        if ([string]::IsNullOrWhiteSpace($path)) {
            $path = Resolve-MyCodexProcessPath -ProcessId $candidate.ProcessId
            if (-not $path) {
                try { $remaining = [Diagnostics.Process]::GetProcessById($candidate.ProcessId); $remaining.Dispose() }
                catch [ArgumentException] { continue }
                # An unreadable process can only be this installation's backend if one of its images is in use.
                if (-not (Test-MyCodexImageLocked $executable) -and
                    -not (Test-MyCodexImageLocked (Join-Path $root 'runtime\auth-center\mycodex-auth-host.exe'))) {
                    Write-Diagnostic ('unreadable_process_ignored pid=' + [int]$candidate.ProcessId)
                    continue
                }
                Write-Diagnostic ('unreadable_process pid=' + [int]$candidate.ProcessId)
                throw 'authentication_process_identity_unavailable'
            }
        }
        $path = Normalize-TargetPath $path
        if ($path -ieq (Join-Path $root 'runtime\auth-center\mycodex-auth-host.exe')) {
            throw 'authentication_legacy_backend_running'
        }
        if ($path -ine $executable) { continue }
        Write-Diagnostic ('owned_process pid=' + [int]$candidate.ProcessId)
        $owner = Invoke-CimMethod -InputObject $candidate -MethodName GetOwnerSid
        if ($owner.ReturnValue -ne 0 -or $owner.Sid -ne $currentSid) { throw 'authentication_update_wrong_user' }
        $parts = [AuthenticationHostArguments]::Parse($candidate.CommandLine)
        if ($parts.Length -ne 6 -or $parts[1] -ne 'serve') { throw 'authentication_update_unknown_process' }
        $options = @{}
        for ($index=2; $index -lt 6; $index+=2) {
            if ($parts[$index] -notin @('--data-dir','--codex-home') -or $options.ContainsKey($parts[$index]) -or -not [IO.Path]::IsPathRooted($parts[$index+1])) { throw 'authentication_update_invalid_target' }
            $options[$parts[$index]] = $parts[$index+1]
        }
        $targets += @{store=$options['--data-dir']; home=$options['--codex-home']; processId=[int]$candidate.ProcessId}
    }
    $runningTargets = $targets
    $unfinished = $false
    if (Test-Path -LiteralPath $stateFile) {
        $previous = Get-Content -Raw -Encoding UTF8 $stateFile | ConvertFrom-Json
        if ($previous.installRoot -ine $root) { throw 'authentication_update_target_mismatch' }
        $unfinished = -not $previous.completed
        if ($unfinished) {
            foreach ($target in $previous.targets) {
                if (@($targets | Where-Object { $_.home -ieq $target.home -and $_.store -ieq $target.store }).Count -eq 0) { $targets += $target }
            }
        }
    }
    $validPackage = Test-PackageHash $packageDirectory
    $backup = $stateFile + '.backup'
    $recoverableBackup = $unfinished -and (Test-PackageHash $backup)
    if (-not $validPackage -and (@($runningTargets).Count -gt 0 -or
        (@($targets).Count -gt 0 -and -not $recoverableBackup) -or
        ($Action -eq 'Uninstall' -and (Test-Path -LiteralPath $executable)))) {
        throw 'authentication_update_invalid_package'
    }
    # Preserve targets and a valid backup from interrupted updates. A broken,
    # unused package must not prevent a repair install or overwrite that backup.
    $state = @{installRoot=$root; targets=$targets; hadPackage=($validPackage -or $recoverableBackup); completed=$false}
    Write-State $state
    if ($validPackage) {
        [IO.Directory]::CreateDirectory($backup) | Out-Null
        if (-not $unfinished -or -not (Test-PackageHash $backup)) {
            foreach ($name in @('mycodex-auth-host.exe','mycodex-auth-host.manifest.json','LICENSE')) {
                Copy-Item -LiteralPath (Join-Path $packageDirectory $name) -Destination (Join-Path $backup $name) -Force
            }
        }
    } else { Write-Diagnostic 'unused_incomplete_package_repair' }
    if ($Action -eq 'Uninstall') {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $key = [BitConverter]::ToString($sha.ComputeHash($serializer.GetBytes((Join-Path $root 'MyCodex.exe').ToUpperInvariant()))).Replace('-','') } finally { $sha.Dispose() }
        $registered = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\MyCodex\AuthenticationHosts\'+$key)
        if ($registered) {
            try {
                foreach ($name in $registered.GetValueNames()) {
                    $paths = $registered.GetValue($name)
                    if ($paths -isnot [string[]] -or $paths.Length -ne 3 -or $paths[0] -ine $executable) { throw 'authentication_update_invalid_registration' }
                    if (@($targets | Where-Object { $_.home -ieq $paths[1] }).Count -eq 0) {
                        $target = @{store=$paths[2];home=$paths[1];processId=0}
                        Start-Host $target
                        $runningTargets += $target
                    }
                }
            } finally { $registered.Dispose() }
        }
    }
    foreach ($target in $runningTargets) {
        Assert-HostIdentity (Invoke-HostRequest $target 'status' @{}) $target
        $parameters = if ($Action -eq 'Prepare') { @{forUpdate=$true} } else { @{forUpdate=$false} }
        Invoke-HostRequest $target 'backend/shutdown' $parameters | Out-Null
        $process = if ($target.processId -gt 0) { Get-Process -Id $target.processId -ErrorAction SilentlyContinue } else { $null }
        if ($process -and -not $process.WaitForExit(15000)) { throw 'authentication_backend_did_not_exit' }
    }
    if ($Action -eq 'Uninstall') {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree(('Software\MyCodex\AuthenticationHosts\'+$key),$false)
        $run = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true)
        if ($run) { try { $run.DeleteValue(('MyCodex.Authentication.'+$key),$false) } finally { $run.Dispose() } }
    }
    Write-Diagnostic 'completed'
    exit 0
} catch {
    # Never echo command lines, stored paths, or upstream exception payloads.
    $code = if ($_.Exception.Message -cmatch '^authentication_[a-z_]+$') { $_.Exception.Message } else { 'authentication_maintenance_failed' }
    Write-Diagnostic $code
    Write-Error ($code + ' (line ' + $_.InvocationInfo.ScriptLineNumber + '): Finish active requests/sign-in; for uninstall, switch to a direct provider first. The backend was not forcibly stopped. Retry before replacing files.') -ErrorAction Continue
    exit 1
}
