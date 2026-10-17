param([Parameter(Mandatory = $true)][string]$InstallRoot, [switch]$FunctionsOnly, [string]$MutexNamePath, [switch]$IncludeManagedProcesses, [string]$DiagnosticPath, [switch]$AllowLegacyNodeCleanup, [string]$UninstallerPath)

$ErrorActionPreference = 'Stop'

function Get-MyCodexUpdateMutexName {
    param([string]$Root)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $directory = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
        $key = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($directory.ToUpperInvariant()))).Replace('-', '')
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $route = $sid + "`nSHAREDUPDATE`n" + $key.ToLowerInvariant()
        return 'Global\MyCodex.Target.' + [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($route))).Replace('-', '')
    } finally { $sha.Dispose() }
}

function Initialize-MyCodexInstallerNative {
    if (-not ('MyCodexInstaller.ProcessImage' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
namespace MyCodexInstaller {
    public static class ProcessImage {
        [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        public static string Read(int id) {
            var handle = OpenProcess(0x1000, false, id);
            if (handle == IntPtr.Zero) return null;
            try { var size = 32768; var name = new StringBuilder(size); return QueryFullProcessImageName(handle, 0, name, ref size) ? name.ToString() : null; }
            finally { CloseHandle(handle); }
        }
    }
}
'@
    }
    if (-not ('MyCodexInstaller.ImageLock' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
namespace MyCodexInstaller {
    public static class ImageLock {
        // Windows refuses write access to an executable or library while any process has it mapped.
        // This answers whether the installation is in use without inspecting process identities.
        public static bool IsLocked(string path) {
            for (var attempt = 0; attempt < 3; attempt++) {
                try {
                    using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { return false; }
                }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
                catch (IOException ex) {
                    var code = ex.HResult & 0xFFFF;
                    if (code != 32 && code != 33) throw;
                    // Retry briefly so a scanner's short-lived handle is not mistaken for a running target.
                    if (attempt < 2) Thread.Sleep(200);
                }
            }
            return true;
        }

        public static string[] FindLocked(string root) {
            var result = new List<string>();
            if (!Directory.Exists(root)) return result.ToArray();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0) {
                var directory = pending.Pop();
                string[] files, directories;
                try { files = Directory.GetFiles(directory); directories = Directory.GetDirectories(directory); }
                catch (DirectoryNotFoundException) { continue; }
                foreach (var child in directories) {
                    // Linked folders can point outside this installation.
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
                foreach (var file in files) {
                    var extension = Path.GetExtension(file);
                    if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".node", StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsLocked(file)) result.Add(file.Substring(root.Length).TrimStart('\\'));
                }
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result.ToArray();
        }
    }
}
'@
    }
}

function Resolve-MyCodexProcessPath {
    param([int]$ProcessId)
    Initialize-MyCodexInstallerNative
    return [MyCodexInstaller.ProcessImage]::Read($ProcessId)
}

function Test-MyCodexImageLocked {
    param([string]$Path)
    Initialize-MyCodexInstallerNative
    return [MyCodexInstaller.ImageLock]::IsLocked([IO.Path]::GetFullPath($Path))
}

function Get-MyCodexLockedImages {
    param([string]$Root)
    Initialize-MyCodexInstallerNative
    return @([MyCodexInstaller.ImageLock]::FindLocked([IO.Path]::GetFullPath($Root).TrimEnd('\')))
}

function Test-MyCodexLegacyNodePath {
    param([string]$Path, [string]$Root)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    $normalized = [IO.Path]::GetFullPath($Path)
    $legacy = (Join-Path ([IO.Path]::GetFullPath($Root)) 'runtime\node') + '\'
    return [IO.Path]::GetFileName($normalized) -ieq 'node.exe' -and $normalized.StartsWith($legacy, [StringComparison]::OrdinalIgnoreCase)
}

function Test-MyCodexGuardedImage {
    # Decides whether a locked image (relative to the installation) blocks the current stage.
    param([string]$RelativePath, [switch]$IncludeManagedProcesses, [switch]$AllowLegacyNodeCleanup)
    $relative = $RelativePath.Replace('/', '\').TrimStart('\')
    if ($IncludeManagedProcesses) { return $true }
    $name = [IO.Path]::GetFileName($relative)
    $inRuntime = $relative.StartsWith('runtime\', [StringComparison]::OrdinalIgnoreCase)
    # Authentication has its own graceful maintenance protocol; Relay is stopped only after clients are absent.
    if ($inRuntime -and $name -in @('mycodex-auth-host.exe', 'mycodex-relay.exe')) { return $false }
    if ($relative -ieq 'relay\mycodex-relay.exe') { return $false }
    # This exception is only for the post-save cleanup stage. The final
    # IncludeManagedProcesses guard must still see every remaining runtime.
    if ($AllowLegacyNodeCleanup -and $name -ieq 'node.exe' -and $relative.StartsWith('runtime\node\', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    return $true
}

function Assert-NoMyCodexResourceUsers {
    param([string]$Root, [string[]]$LockedImages, [switch]$IncludeManagedProcesses, [string]$DiagnosticPath, [switch]$AllowLegacyNodeCleanup, [string]$UninstallerPath)
    $resolved = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $uninstallerName = $null
    if ($UninstallerPath) {
        $uninstaller = [IO.Path]::GetFullPath($UninstallerPath)
        $uninstallerName = [IO.Path]::GetFileName($uninstaller)
        if ([IO.Path]::GetDirectoryName($uninstaller) -ine $resolved -or $uninstallerName -notmatch '^unins[0-9]+\.exe$') { throw 'invalid_uninstaller_path' }
    }
    if ($null -eq $LockedImages) { $LockedImages = @(Get-MyCodexLockedImages -Root $resolved) }
    $busy = $false
    $details = [Collections.Generic.List[string]]::new()
    foreach ($relative in $LockedImages) {
        # Inno's original uninstaller remains mapped while its temporary child removes files.
        # Exempt only that exact root image, only during uninstall; Setup still checks it.
        if ($uninstallerName -and $relative -ieq $uninstallerName) { continue }
        if (-not (Test-MyCodexGuardedImage -RelativePath $relative -IncludeManagedProcesses:$IncludeManagedProcesses -AllowLegacyNodeCleanup:$AllowLegacyNodeCleanup)) { continue }
        $busy = $true
        if ($details.Count -lt 20) {
            # Paths are relative to this installation; no process, account or command-line data is recorded.
            $text = $relative -replace '[\r\n\t]', '?'
            if ($text.Length -gt 240) { $text = $text.Substring(0, 240) }
            $details.Add("in_use path=$text")
        }
    }
    if ($DiagnosticPath) {
        try { [IO.File]::WriteAllLines($DiagnosticPath, $details.ToArray(), [Text.UTF8Encoding]::new($false)) } catch { }
    }
    if ($busy) { throw 'mycodex_resource_still_in_use' }
}

if (-not $FunctionsOnly) {
    try {
        if ($MutexNamePath) {
            [IO.File]::WriteAllText($MutexNamePath, (Get-MyCodexUpdateMutexName $InstallRoot), [Text.Encoding]::ASCII)
        } else { Assert-NoMyCodexResourceUsers -Root $InstallRoot -IncludeManagedProcesses:$IncludeManagedProcesses -DiagnosticPath $DiagnosticPath -AllowLegacyNodeCleanup:$AllowLegacyNodeCleanup -UninstallerPath $UninstallerPath }
        exit 0
    } catch {
        if ($_.Exception.Message -ceq 'mycodex_resource_still_in_use') {
            [Console]::Error.WriteLine('mycodex_resource_still_in_use'); exit 1
        }
        [Console]::Error.WriteLine('resource_inspection_failed'); exit 2
    }
}
