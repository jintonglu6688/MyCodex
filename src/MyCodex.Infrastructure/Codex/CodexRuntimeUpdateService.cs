using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.Updates;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Codex
{
    public sealed class CodexRuntimeUpdateService
    {
        private readonly AppConfigService _appConfigService;
        private readonly CodexRuntimePackageClient _packages;
        private readonly Func<IEnumerable<string>> _runningExecutablePaths;

        public CodexRuntimeUpdateService(AppConfigService appConfigService)
            : this(appConfigService, new CodexRuntimePackageClient(), null)
        {
        }

        internal CodexRuntimeUpdateService(AppConfigService appConfigService, Func<IEnumerable<string>> runningExecutablePaths)
            : this(appConfigService, new CodexRuntimePackageClient(), runningExecutablePaths)
        {
        }

        internal CodexRuntimeUpdateService(AppConfigService appConfigService, CodexRuntimePackageClient packages,
            Func<IEnumerable<string>> runningExecutablePaths = null)
        {
            _appConfigService = appConfigService ?? throw new ArgumentNullException(nameof(appConfigService));
            _packages = packages ?? throw new ArgumentNullException(nameof(packages));
            _runningExecutablePaths = runningExecutablePaths;
        }

        public CodexRuntimeUpdateInfo GetInstalledInfo()
        {
            return GetInstalledInfo(CodexRuntimeSources.Bundled);
        }

        public CodexRuntimeUpdateInfo GetInstalledInfo(string runtimeSource)
        {
            var targetTriple = GetCurrentTargetTriple();
            var installedVersion = ResolveInstalledVersion(runtimeSource, targetTriple);
            return new CodexRuntimeUpdateInfo
            {
                TargetTriple = targetTriple,
                InstalledVersion = installedVersion,
                LatestVersion = installedVersion,
                IsLatestInstalled = !string.IsNullOrWhiteSpace(installedVersion)
            };
        }

        public async Task<CodexRuntimeUpdateInfo> CheckLatestAsync(CancellationToken cancellationToken)
        {
            return await CheckLatestAsync(CodexRuntimeSources.Bundled, cancellationToken).ConfigureAwait(false);
        }

        public async Task<CodexRuntimeUpdateInfo> CheckLatestAsync(string runtimeSource, CancellationToken cancellationToken)
        {
            var targetTriple = GetCurrentTargetTriple();
            var package = await _packages.CheckLatestAsync(targetTriple, cancellationToken).ConfigureAwait(false);
            var latestVersion = package.Version;
            var installedVersion = ResolveInstalledVersion(runtimeSource, targetTriple);

            return new CodexRuntimeUpdateInfo
            {
                TargetTriple = targetTriple,
                InstalledVersion = installedVersion,
                LatestVersion = latestVersion,
                Package = package,
                IsLatestInstalled = string.Equals(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(_appConfigService.GetBundledCodexExecutablePath(latestVersion, targetTriple))
            };
        }

        public async Task<CodexRuntimeUpdateResult> InstallLatestAsync(
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            return await InstallLatestAsync(CodexRuntimeSources.Bundled, progress, cancellationToken).ConfigureAwait(false);
        }

        public async Task<CodexRuntimeUpdateResult> InstallLatestAsync(
            string runtimeSource,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            return await InstallLatestAsync(runtimeSource, progress, cancellationToken, false).ConfigureAwait(false);
        }

        public async Task<CodexRuntimeUpdateResult> InstallLatestAsync(
            string runtimeSource,
            IProgress<string> progress,
            CancellationToken cancellationToken,
            bool forceReinstall)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var reservation = new SharedRuntimeMutationReservation(_appConfigService.GetBundledCodexDirectory(), _runningExecutablePaths))
                return await InstallLatestCoreAsync(runtimeSource, progress, cancellationToken, forceReinstall, reservation).ConfigureAwait(false);
        }

        public async Task<CodexRuntimeUpdateResult> InstallCheckedAsync(CodexRuntimeUpdateInfo info,
            IProgress<string> progress, IProgress<UpdateDownloadProgress> downloadProgress, CancellationToken cancellationToken)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            if (info.TargetTriple != GetCurrentTargetTriple() || string.IsNullOrWhiteSpace(info.LatestVersion))
                throw new ArgumentException("The checked runtime package does not match this platform.", nameof(info));
            cancellationToken.ThrowIfCancellationRequested();
            using (var reservation = new SharedRuntimeMutationReservation(_appConfigService.GetBundledCodexDirectory(), _runningExecutablePaths))
                return await InstallLatestCoreAsync(CodexRuntimeSources.Bundled, progress, cancellationToken, false,
                    reservation, info, downloadProgress).ConfigureAwait(false);
        }

        private async Task<CodexRuntimeUpdateResult> InstallLatestCoreAsync(
            string runtimeSource,
            IProgress<string> progress,
            CancellationToken cancellationToken,
            bool forceReinstall,
            SharedRuntimeMutationReservation reservation,
            CodexRuntimeUpdateInfo checkedInfo = null,
            IProgress<UpdateDownloadProgress> downloadProgress = null)
        {
            Report(progress, SharedLocalization.Text(
                "CodexRuntimeUpdate.Progress.CheckingLatest",
                "Checking latest Codex runtime..."));

            var info = checkedInfo ?? await CheckLatestAsync(runtimeSource, cancellationToken).ConfigureAwait(false);
            if (!forceReinstall)
            {
                // Another window may have published the package while this one was waiting.
                var installed = GetInstalledInfo(runtimeSource);
                if (!string.IsNullOrWhiteSpace(installed.InstalledVersion) &&
                    UpdateVersionComparer.Compare(installed.InstalledVersion, info.LatestVersion) >= 0)
                    return new CodexRuntimeUpdateResult
                    {
                        TargetTriple = installed.TargetTriple, Version = installed.InstalledVersion,
                        ExecutablePath = _appConfigService.GetBundledCodexExecutablePath(installed.InstalledVersion, installed.TargetTriple),
                        PreviousVersion = info.InstalledVersion
                    };
            }
            var metadata = info.Package;
            if (metadata == null || metadata.Version != info.LatestVersion || metadata.TargetTriple != info.TargetTriple)
                throw new InvalidOperationException(SharedLocalization.Text("CodexRuntimeUpdate.Error.PackageChanged",
                    "Runtime update metadata changed during installation. Check for updates again."));

            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "MyCodex-CodexRuntime-" + Guid.NewGuid().ToString("N"));
            var archivePath = Path.Combine(tempRoot, metadata.FileName);
            var extractRoot = Path.Combine(tempRoot, "extract");
            var destinationPath = _appConfigService.GetBundledCodexInstallDirectory(info.LatestVersion, info.TargetTriple);
            EnsurePathUnderBundledRuntimeRoot(destinationPath);

            try
            {
                Directory.CreateDirectory(tempRoot);
                Directory.CreateDirectory(extractRoot);

                await _packages.DownloadVerifiedAsync(metadata, archivePath, downloadProgress, cancellationToken, progress).ConfigureAwait(false);

                Report(progress, SharedLocalization.Text(
                    "CodexRuntimeUpdate.Progress.Extracting",
                    "Extracting Codex runtime package..."));
                await ExtractPackageAsync(archivePath, extractRoot, cancellationToken).ConfigureAwait(false);

                var packageRoot = Path.Combine(extractRoot, "package");
                var codexExe = ResolvePackageCodexExecutablePath(packageRoot, info.TargetTriple);
                if (!File.Exists(codexExe))
                {
                    throw new InvalidOperationException(string.Format(
                        CultureInfo.CurrentCulture,
                        SharedLocalization.Text(
                            "CodexRuntimeUpdate.Error.ExecutableMissing.Template",
                            "Codex runtime package did not contain {0}."),
                        codexExe));
                }

                Report(progress, SharedLocalization.Text(
                    "CodexRuntimeUpdate.Progress.Installing",
                    "Installing Codex runtime..."));
                var updated = reservation.PublishPackage(packageRoot, destinationPath, info.TargetTriple, forceReinstall, cancellationToken);

                return new CodexRuntimeUpdateResult
                {
                    TargetTriple = info.TargetTriple,
                    Version = info.LatestVersion,
                    ExecutablePath = _appConfigService.GetBundledCodexExecutablePath(info.LatestVersion, info.TargetTriple),
                    WasUpdated = updated,
                    PreviousVersion = info.InstalledVersion
                };
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private string ResolveInstalledVersion(string runtimeSource, string targetTriple)
        {
            return _appConfigService.GetLatestInstalledBundledCodexVersion(targetTriple);
        }

        internal static string ResolvePackageCodexExecutablePath(string packageRoot, string targetTriple)
        {
            var currentPath = Path.Combine(packageRoot, "vendor", targetTriple, "bin", "codex.exe");
            if (File.Exists(currentPath))
            {
                return currentPath;
            }

            return Path.Combine(packageRoot, "vendor", targetTriple, "codex", "codex.exe");
        }

        public IReadOnlyList<string> RemoveObsoleteInstalledVersions(string keepVersion, string targetTriple)
        {
            if (string.IsNullOrWhiteSpace(keepVersion))
            {
                throw new ArgumentException("Version cannot be empty.", nameof(keepVersion));
            }

            if (string.IsNullOrWhiteSpace(targetTriple))
            {
                throw new ArgumentException("Target platform cannot be empty.", nameof(targetTriple));
            }

            AppConfigService.ValidateRuntimePathSegment(keepVersion, nameof(keepVersion));
            AppConfigService.ValidateRuntimePathSegment(targetTriple, nameof(targetTriple));

            var removedVersions = new List<string>();
            var bundledCodexDirectory = _appConfigService.GetBundledCodexDirectory();
            if (!Directory.Exists(bundledCodexDirectory))
            {
                return removedVersions;
            }

            using (var reservation = new SharedRuntimeMutationReservation(bundledCodexDirectory, _runningExecutablePaths))
            foreach (var versionDirectory in Directory.GetDirectories(bundledCodexDirectory))
            {
                var versionName = Path.GetFileName(versionDirectory);
                if (!AppConfigService.TryValidateRuntimePathSegment(versionName, out versionName) ||
                    string.Equals(versionName, keepVersion, StringComparison.OrdinalIgnoreCase) ||
                    UpdateVersionComparer.Compare(versionName, keepVersion) > 0)
                {
                    continue;
                }

                var installDirectory = _appConfigService.GetBundledCodexInstallDirectory(versionName, targetTriple);
                EnsurePathUnderBundledRuntimeRoot(installDirectory);
                if (!Directory.Exists(installDirectory))
                {
                    continue;
                }

                if (!reservation.RemoveUnusedVersion(installDirectory)) continue;
                if (Directory.Exists(versionDirectory) &&
                    Directory.GetFileSystemEntries(versionDirectory).Length == 0)
                {
                    Directory.Delete(versionDirectory, false);
                }

                removedVersions.Add(versionName);
            }

            return removedVersions;
        }

        private void EnsurePathUnderBundledRuntimeRoot(string path)
        {
            var root = Path.GetFullPath(_appConfigService.GetBundledCodexDirectory())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "CodexRuntimeUpdate.Error.InstallPathEscaped",
                    "Codex runtime install path escapes the bundled runtime directory."));
            }
        }

        private static async Task ExtractPackageAsync(
            string archivePath,
            string extractRoot,
            CancellationToken cancellationToken)
        {
            var listing = await RunTarAsync(
                "-tzf " + QuoteArgument(archivePath),
                true,
                cancellationToken).ConfigureAwait(false);
            EnsureSafeArchiveListing(listing);

            await RunTarAsync(
                "-xzf " + QuoteArgument(archivePath) + " -C " + QuoteArgument(extractRoot),
                false,
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> RunTarAsync(
            string arguments,
            bool captureStandardOutput,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startInfo = new ProcessStartInfo
            {
                FileName = ResolveTarExecutablePath(),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = captureStandardOutput,
                RedirectStandardError = true
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException(SharedLocalization.Text(
                        "CodexRuntimeUpdate.Error.ExtractFailed",
                        "Failed to extract Codex runtime package."));
                }

                var standardOutputTask = captureStandardOutput
                    ? process.StandardOutput.ReadToEndAsync()
                    : Task.FromResult(string.Empty);
                var standardErrorTask = process.StandardError.ReadToEndAsync();
                try
                {
                    while (!process.WaitForExit(100))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                }
                catch (OperationCanceledException)
                {
                    TryKillProcess(process);
                    throw;
                }

                if (process.ExitCode != 0)
                {
                    var error = await standardErrorTask.ConfigureAwait(false);
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? SharedLocalization.Text(
                            "CodexRuntimeUpdate.Error.ExtractFailed",
                            "Failed to extract Codex runtime package.")
                        : error.Trim());
                }

                return await standardOutputTask.ConfigureAwait(false);
            }
        }

        // Use the inbox bsdtar so a tar.exe in the application or current directory is never picked up.
        internal static string ResolveTarExecutablePath()
        {
            var systemTar = Path.Combine(Environment.SystemDirectory, "tar.exe");
            return File.Exists(systemTar) ? systemTar : "tar.exe";
        }

        private static void EnsureSafeArchiveListing(string listing)
        {
            using (var reader = new StringReader(listing ?? string.Empty))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!IsSafeArchiveEntryName(line))
                    {
                        throw new InvalidOperationException(SharedLocalization.Text(
                            "CodexRuntimeUpdate.Error.UnsafeArchiveEntry",
                            "Codex runtime archive contains an unsafe entry path."));
                    }
                }
            }
        }

        private static bool IsSafeArchiveEntryName(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                return false;
            }

            var normalized = entryName.Replace('\\', '/').Trim();
            if (normalized.Length == 0 ||
                normalized.StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            if (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
            {
                return false;
            }

            var segments = normalized.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                if (string.Equals(segments[i], "..", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetCurrentTargetTriple()
        {
            var primaryArchitecture = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty).Trim();
            var redirectedArchitecture = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? string.Empty).Trim();
            if (string.Equals(primaryArchitecture, "ARM64", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(redirectedArchitecture, "ARM64", StringComparison.OrdinalIgnoreCase))
            {
                return "aarch64-pc-windows-msvc";
            }

            return "x86_64-pc-windows-msvc";
        }

        private static string QuoteArgument(string argument)
        {
            return WindowsCommandLineArgumentEscaper.Quote(argument);
        }

        private static void Report(IProgress<string> progress, string message)
        {
            if (progress != null)
            {
                progress.Report(message);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Temporary extraction leftovers should not hide the real update result.
            }
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Cancellation cleanup is best-effort.
            }
        }
    }
}
