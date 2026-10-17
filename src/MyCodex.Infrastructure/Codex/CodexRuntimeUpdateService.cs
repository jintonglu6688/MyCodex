using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.Updates;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Codex
{
    public sealed class CodexRuntimeUpdateService
    {
        private const string RegistryBaseUrl = "https://registry.npmjs.org";
        private readonly AppConfigService _appConfigService;
        private readonly UpdateManifestClient _manifestClient;
        private readonly UpdatePackageDownloader _updatePackageDownloader;
        private readonly Func<IEnumerable<string>> _runningExecutablePaths;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        public CodexRuntimeUpdateService(AppConfigService appConfigService)
            : this(appConfigService, (UpdateSettingsSnapshot)null)
        {
        }

        public CodexRuntimeUpdateService(AppConfigService appConfigService, UpdateSettingsSnapshot updateSettings)
            : this(
                  appConfigService,
                  new UpdateManifestClient(updateSettings),
                  new UpdatePackageDownloader())
        {
        }

        public CodexRuntimeUpdateService(
            AppConfigService appConfigService,
            UpdateManifestClient manifestClient,
            UpdatePackageDownloader updatePackageDownloader)
            : this(appConfigService, manifestClient, updatePackageDownloader, null)
        {
        }

        internal CodexRuntimeUpdateService(AppConfigService appConfigService, Func<IEnumerable<string>> runningExecutablePaths)
            : this(appConfigService, new UpdateManifestClient((UpdateSettingsSnapshot)null), new UpdatePackageDownloader(), runningExecutablePaths)
        {
        }

        private CodexRuntimeUpdateService(
            AppConfigService appConfigService,
            UpdateManifestClient manifestClient,
            UpdatePackageDownloader updatePackageDownloader,
            Func<IEnumerable<string>> runningExecutablePaths)
        {
            _appConfigService = appConfigService ?? throw new ArgumentNullException(nameof(appConfigService));
            _manifestClient = manifestClient ?? throw new ArgumentNullException(nameof(manifestClient));
            _updatePackageDownloader = updatePackageDownloader ?? throw new ArgumentNullException(nameof(updatePackageDownloader));
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
            var manifest = _manifestClient.IsConfigured
                ? await LoadRuntimeManifestAsync(runtimeSource, targetTriple, cancellationToken).ConfigureAwait(false) : null;
            var latestVersion = manifest != null ? manifest.FindRuntimePackage(runtimeSource, targetTriple).Version.Trim()
                : await GetLatestVersionAsync(cancellationToken).ConfigureAwait(false);
            var installedVersion = ResolveInstalledVersion(runtimeSource, targetTriple);

            return new CodexRuntimeUpdateInfo
            {
                TargetTriple = targetTriple,
                InstalledVersion = installedVersion,
                LatestVersion = latestVersion,
                Manifest = manifest,
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
            if (checkedInfo != null)
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
            if (info.IsLatestInstalled && !forceReinstall)
            {
                return new CodexRuntimeUpdateResult
                {
                    TargetTriple = info.TargetTriple,
                    Version = info.LatestVersion,
                    ExecutablePath = _appConfigService.GetBundledCodexExecutablePath(info.LatestVersion, info.TargetTriple),
                    WasUpdated = false,
                    PreviousVersion = info.InstalledVersion
                };
            }

            if (_manifestClient.IsConfigured)
            {
                return await InstallLatestFromManifestAsync(
                    runtimeSource,
                    info,
                    progress,
                    cancellationToken,
                    forceReinstall,
                    reservation, downloadProgress).ConfigureAwait(false);
            }

            var metadata = await GetPlatformPackageMetadataAsync(
                info.LatestVersion,
                info.TargetTriple,
                cancellationToken).ConfigureAwait(false);

            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "MyCodex-CodexRuntime-" + Guid.NewGuid().ToString("N"));
            var archivePath = Path.Combine(tempRoot, metadata.ExpectedFileName);
            var extractRoot = Path.Combine(tempRoot, "extract");
            var destinationPath = _appConfigService.GetBundledCodexInstallDirectory(info.LatestVersion, info.TargetTriple);
            EnsurePathUnderBundledRuntimeRoot(destinationPath);

            try
            {
                Directory.CreateDirectory(tempRoot);
                Directory.CreateDirectory(extractRoot);

                Report(progress, string.Format(
                    CultureInfo.CurrentCulture,
                    SharedLocalization.Text(
                        "CodexRuntimeUpdate.Progress.Downloading.Template",
                        "Downloading Codex runtime {0}..."),
                    info.LatestVersion));
                await DownloadFileAsync(metadata.TarballUrl, archivePath, cancellationToken, downloadProgress).ConfigureAwait(false);

                Report(progress, SharedLocalization.Text(
                    "CodexRuntimeUpdate.Progress.Verifying",
                    "Verifying Codex runtime package..."));
                if (!VerifyPackageIntegrity(archivePath, metadata.Integrity, metadata.ShaSum))
                {
                    throw new InvalidOperationException(SharedLocalization.Text(
                        "CodexRuntimeUpdate.Error.IntegrityCheckFailed",
                        "Codex runtime package integrity check failed."));
                }

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

        private async Task<UpdateManifest> LoadRuntimeManifestAsync(
            string runtimeSource,
            string targetTriple,
            CancellationToken cancellationToken)
        {
            var manifest = await _manifestClient.LoadAsync(cancellationToken).ConfigureAwait(false);
            var runtimePackage = manifest.FindRuntimePackage(runtimeSource, targetTriple);
            if (runtimePackage == null || runtimePackage.Asset == null || string.IsNullOrWhiteSpace(runtimePackage.Version))
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.CurrentCulture,
                    SharedLocalization.Text(
                        "CodexRuntimeUpdate.Error.ManifestRuntimeMissing.Template",
                        "Update manifest does not include runtime package {0} for {1}."),
                    runtimeSource,
                    targetTriple));
            }

            return manifest;
        }

        private async Task<CodexRuntimeUpdateResult> InstallLatestFromManifestAsync(
            string runtimeSource,
            CodexRuntimeUpdateInfo info,
            IProgress<string> progress,
            CancellationToken cancellationToken,
            bool forceReinstall,
            SharedRuntimeMutationReservation reservation,
            IProgress<UpdateDownloadProgress> downloadProgress = null)
        {
            // Retain the checked version and checksum while newer releases are published.
            var manifest = info.Manifest ?? await _manifestClient.LoadAsync(cancellationToken).ConfigureAwait(false);
            var runtimePackage = manifest.FindRuntimePackage(runtimeSource, info.TargetTriple);
            if (runtimePackage == null || runtimePackage.Asset == null)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.CurrentCulture,
                    SharedLocalization.Text(
                        "CodexRuntimeUpdate.Error.ManifestRuntimeMissing.Template",
                        "Update manifest does not include runtime package {0} for {1}."),
                    runtimeSource,
                    info.TargetTriple));
            }
            if (!string.Equals(runtimePackage.Version?.Trim(), info.LatestVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(SharedLocalization.Text("CodexRuntimeUpdate.Error.ManifestChanged",
                    "Runtime update metadata changed during installation. Check for updates again."));

            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "MyCodex-CodexRuntime-" + Guid.NewGuid().ToString("N"));
            var extractRoot = Path.Combine(tempRoot, "extract");
            var destinationPath = _appConfigService.GetBundledCodexInstallDirectory(info.LatestVersion, info.TargetTriple);
            EnsurePathUnderBundledRuntimeRoot(destinationPath);

            try
            {
                Directory.CreateDirectory(tempRoot);
                Directory.CreateDirectory(extractRoot);

                Report(progress, string.Format(
                    CultureInfo.CurrentCulture,
                    SharedLocalization.Text(
                        "CodexRuntimeUpdate.Progress.Downloading.Template",
                        "Downloading Codex runtime {0}..."),
                    info.LatestVersion));
                var archivePath = await _updatePackageDownloader.DownloadVerifiedAsync(
                    _manifestClient.ResolveAssetUri(runtimePackage.Asset, manifest),
                    runtimePackage.Asset,
                    tempRoot,
                    downloadProgress,
                    cancellationToken).ConfigureAwait(false);

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

        private async Task<string> GetLatestVersionAsync(CancellationToken cancellationToken)
        {
            var metadataUrl = RegistryBaseUrl.TrimEnd('/') + "/%40openai%2Fcodex/latest";
            var metadata = await DownloadJsonAsync(metadataUrl, cancellationToken).ConfigureAwait(false);
            var version = GetRequiredString(metadata, "version");
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "CodexRuntimeUpdate.Error.LatestVersionMissing",
                    "Codex runtime metadata did not include the latest version."));
            }

            return version.Trim();
        }

        private async Task<PlatformPackageMetadata> GetPlatformPackageMetadataAsync(
            string version,
            string targetTriple,
            CancellationToken cancellationToken)
        {
            var packageSuffix = GetPackageSuffix(targetTriple);
            var packageVersionTag = version + "-" + packageSuffix;

            var candidates = new[]
            {
                new
                {
                    MetadataUrl = RegistryBaseUrl.TrimEnd('/') + "/%40openai%2Fcodex/" + packageVersionTag,
                    ExpectedFileName = "codex-" + packageVersionTag + ".tgz"
                },
                new
                {
                    MetadataUrl = RegistryBaseUrl.TrimEnd('/') + "/%40openai%2Fcodex/" + version,
                    ExpectedFileName = "codex-" + version + ".tgz"
                }
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    var metadata = await DownloadJsonAsync(candidate.MetadataUrl, cancellationToken).ConfigureAwait(false);
                    var dist = GetRequiredDictionary(metadata, "dist");
                    var tarballUrl = GetRequiredString(dist, "tarball");
                    var tarballFileName = Path.GetFileName(new Uri(tarballUrl).AbsolutePath);
                    if (!string.Equals(tarballFileName, candidate.ExpectedFileName, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(string.Format(
                            CultureInfo.CurrentCulture,
                            SharedLocalization.Text(
                                "CodexRuntimeUpdate.Error.UnexpectedPackageName.Template",
                                "Unexpected Codex runtime package name '{0}'. Expected '{1}'."),
                            tarballFileName,
                            candidate.ExpectedFileName));
                    }

                    return new PlatformPackageMetadata
                    {
                        TarballUrl = tarballUrl,
                        Integrity = GetOptionalString(dist, "integrity"),
                        ShaSum = GetOptionalString(dist, "shasum"),
                        ExpectedFileName = candidate.ExpectedFileName
                    };
                }
                catch (WebException ex)
                {
                    var response = ex.Response as HttpWebResponse;
                    if (response == null || response.StatusCode != HttpStatusCode.NotFound)
                    {
                        throw;
                    }
                }
            }

            throw new InvalidOperationException(string.Format(
                CultureInfo.CurrentCulture,
                SharedLocalization.Text(
                    "CodexRuntimeUpdate.Error.VersionMetadataMissing.Template",
                    "Codex runtime metadata was not found for version {0}."),
                version));
        }

        private async Task<Dictionary<string, object>> DownloadJsonAsync(string url, CancellationToken cancellationToken)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new WebClient())
            using (cancellationToken.Register(client.CancelAsync))
            {
                try
                {
                    var json = await client.DownloadStringTaskAsync(new Uri(url)).ConfigureAwait(false);
                    return _serializer.Deserialize<Dictionary<string, object>>(json);
                }
                catch (WebException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }

        private static async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken,
            IProgress<UpdateDownloadProgress> progress = null)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new WebClient())
            using (cancellationToken.Register(client.CancelAsync))
            {
                if (progress != null)
                    client.DownloadProgressChanged += (sender, e) => progress.Report(new UpdateDownloadProgress
                    {
                        Percentage = e.ProgressPercentage, BytesReceived = e.BytesReceived,
                        TotalBytesToReceive = e.TotalBytesToReceive
                    });
                try
                {
                    await client.DownloadFileTaskAsync(UpdateManifestClient.EnsureTrustedUpdateUri(new Uri(url)), destinationPath).ConfigureAwait(false);
                }
                catch (WebException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
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

        // A published SHA-256/384/512 SRI hash is authoritative: a mismatch must not be rescued by
        // the weaker SHA-1 shasum. SHA-1 is used only when no strong SRI hash is published.
        internal static bool VerifyPackageIntegrity(string filePath, string integrity, string shaSum)
        {
            bool hasStrongHash;
            if (TestSriIntegrity(filePath, integrity, out hasStrongHash))
            {
                return true;
            }

            return !hasStrongHash && TestSha1(filePath, shaSum);
        }

        private static bool TestSriIntegrity(string filePath, string integrity, out bool hasStrongHash)
        {
            hasStrongHash = false;
            if (string.IsNullOrWhiteSpace(integrity))
            {
                return false;
            }

            var weakMatch = false;
            foreach (var token in integrity.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = token.IndexOf("-", StringComparison.Ordinal);
                if (separator <= 0 || separator >= token.Length - 1)
                {
                    continue;
                }

                var algorithmName = token.Substring(0, separator).ToUpperInvariant();
                var expected = token.Substring(separator + 1);
                var actual = ComputeHashBase64(filePath, algorithmName);
                if (actual == null)
                {
                    continue;
                }

                var isStrong = !string.Equals(algorithmName, "SHA1", StringComparison.Ordinal);
                hasStrongHash |= isStrong;
                if (string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    if (isStrong)
                    {
                        return true;
                    }

                    weakMatch = true;
                }
            }

            return weakMatch && !hasStrongHash;
        }

        private static bool TestSha1(string filePath, string shaSum)
        {
            if (string.IsNullOrWhiteSpace(shaSum))
            {
                return false;
            }

            return string.Equals(
                ComputeHashHex(filePath, "SHA1"),
                shaSum.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeHashBase64(string filePath, string algorithmName)
        {
            var hash = ComputeHash(filePath, algorithmName);
            return hash == null ? null : Convert.ToBase64String(hash);
        }

        private static string ComputeHashHex(string filePath, string algorithmName)
        {
            var hash = ComputeHash(filePath, algorithmName);
            if (hash == null)
            {
                return null;
            }

            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }

        private static byte[] ComputeHash(string filePath, string algorithmName)
        {
            HashAlgorithm algorithm;
            switch (algorithmName)
            {
                case "SHA1":
                    algorithm = SHA1.Create();
                    break;
                case "SHA256":
                    algorithm = SHA256.Create();
                    break;
                case "SHA384":
                    algorithm = SHA384.Create();
                    break;
                case "SHA512":
                    algorithm = SHA512.Create();
                    break;
                default:
                    return null;
            }

            using (algorithm)
            using (var stream = File.OpenRead(filePath))
            {
                return algorithm.ComputeHash(stream);
            }
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

        private static string GetPackageSuffix(string targetTriple)
        {
            return string.Equals(targetTriple, "aarch64-pc-windows-msvc", StringComparison.OrdinalIgnoreCase)
                ? "win32-arm64"
                : "win32-x64";
        }

        private static Dictionary<string, object> GetRequiredDictionary(
            IDictionary<string, object> values,
            string name)
        {
            object value;
            var dictionary = values != null && values.TryGetValue(name, out value)
                ? value as Dictionary<string, object>
                : null;
            if (dictionary == null)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.CurrentCulture,
                    SharedLocalization.Text(
                        "CodexRuntimeUpdate.Error.MetadataMissing.Template",
                        "Codex runtime metadata did not include {0}."),
                    name));
            }

            return dictionary;
        }

        private static string GetRequiredString(IDictionary<string, object> values, string name)
        {
            var value = GetOptionalString(values, name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.CurrentCulture,
                    SharedLocalization.Text(
                        "CodexRuntimeUpdate.Error.MetadataMissing.Template",
                        "Codex runtime metadata did not include {0}."),
                    name));
            }

            return value;
        }

        private static string GetOptionalString(IDictionary<string, object> values, string name)
        {
            object value;
            return values != null && values.TryGetValue(name, out value)
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : null;
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

        private sealed class PlatformPackageMetadata
        {
            public string TarballUrl { get; set; }

            public string Integrity { get; set; }

            public string ShaSum { get; set; }

            public string ExpectedFileName { get; set; }
        }
    }
}
