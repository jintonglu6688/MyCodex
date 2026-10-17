using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Helpers;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Updates
{
    public sealed class AppUpdateService
    {
        public static ProcessStartInfo CreateInstallerStartInfo(string installerPath, string installationDirectory)
        {
            var installer = Path.GetFullPath(installerPath ?? throw new ArgumentNullException(nameof(installerPath)));
            if (!File.Exists(installer) || !string.Equals(Path.GetExtension(installer), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(SharedLocalization.Text(
                    "AppUpdate.Error.InstallerMissing", "The downloaded installer is unavailable. Download the update again."));
            var installation = Path.GetFullPath(installationDirectory ?? throw new ArgumentNullException(nameof(installationDirectory)))
                .TrimEnd(Path.DirectorySeparatorChar);
            // Setup owns save/cancel, the installation mutex and the final resource check.
            // Starting it must not depend on the client's target discovery or exit dialog.
            return new ProcessStartInfo
            {
                FileName = installer,
                Arguments = "/SP- /DIR=" + WindowsCommandLineArgumentEscaper.Quote(installation),
                UseShellExecute = true
            };
        }

        private readonly AppConfigService _appConfigService;
        private readonly UpdateManifestClient _manifestClient;
        private readonly UpdatePackageDownloader _downloader;

        public AppUpdateService(AppConfigService appConfigService, UpdateSettingsSnapshot settings)
            : this(
                  appConfigService,
                  new UpdateManifestClient(settings),
                  new UpdatePackageDownloader())
        {
        }

        public AppUpdateService(
            AppConfigService appConfigService,
            UpdateManifestClient manifestClient,
            UpdatePackageDownloader downloader)
        {
            _appConfigService = appConfigService ?? throw new ArgumentNullException(nameof(appConfigService));
            _manifestClient = manifestClient ?? throw new ArgumentNullException(nameof(manifestClient));
            _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        }

        public async Task<AppUpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken)
        {
            currentVersion = (currentVersion ?? string.Empty).Trim();
            if (!_manifestClient.IsConfigured)
            {
                return new AppUpdateCheckResult
                {
                    CurrentVersion = currentVersion,
                    IsConfigured = false,
                    IsUpdateAvailable = false
                };
            }

            var manifest = await _manifestClient.LoadAsync(cancellationToken).ConfigureAwait(false);
            var appPackage = manifest.App;
            if (appPackage == null || string.IsNullOrWhiteSpace(appPackage.Version))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "AppUpdate.Error.AppPackageMissing",
                    "Update manifest does not include an app package."));
            }

            return new AppUpdateCheckResult
            {
                CurrentVersion = currentVersion,
                LatestVersion = appPackage.Version.Trim(),
                IsConfigured = true,
                IsUpdateAvailable = UpdateVersionComparer.Compare(appPackage.Version, currentVersion) > 0,
                ReleaseNotes = appPackage.ReleaseNotes,
                ReleaseNotesZhCn = appPackage.ReleaseNotesZhCn,
                ReleaseNotesEnUs = appPackage.ReleaseNotesEnUs,
                Manifest = manifest,
                Package = appPackage
            };
        }

        public async Task<AppUpdateDownloadResult> DownloadAsync(
            AppUpdateCheckResult checkResult,
            CancellationToken cancellationToken)
        {
            return await DownloadAsync(checkResult, null, cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppUpdateDownloadResult> DownloadAsync(
            AppUpdateCheckResult checkResult,
            IProgress<UpdateDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            if (checkResult == null)
            {
                throw new ArgumentNullException(nameof(checkResult));
            }

            if (checkResult.Manifest == null || checkResult.Package == null || checkResult.Package.Asset == null)
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "AppUpdate.Error.AppAssetMissing",
                    "Update manifest does not include an app installer asset."));
            }

            var updateDirectory = Path.Combine(
                _appConfigService.GetUpdateTempDirectory(),
                string.IsNullOrWhiteSpace(checkResult.Package.Version) ? "app" : checkResult.Package.Version.Trim());
            var sourceUri = _manifestClient.ResolveAssetUri(checkResult.Package.Asset, checkResult.Manifest);
            var installerPath = await _downloader.DownloadVerifiedAsync(
                sourceUri,
                checkResult.Package.Asset,
                updateDirectory,
                progress,
                cancellationToken).ConfigureAwait(false);

            return new AppUpdateDownloadResult
            {
                Version = checkResult.Package.Version,
                InstallerPath = installerPath
            };
        }
    }
}
