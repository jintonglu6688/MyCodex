using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Markets
{
    internal sealed class MarketPackageDownloader
    {
        private readonly Func<Uri, string, CancellationToken, Task> _downloadFileAsync;

        internal MarketPackageDownloader()
            : this(DownloadFileAsync)
        {
        }

        internal MarketPackageDownloader(Func<Uri, string, CancellationToken, Task> downloadFileAsync)
        {
            _downloadFileAsync = downloadFileAsync ?? DownloadFileAsync;
        }

        internal async Task<string> DownloadAsync(
            MarketPackageDownloadSpec spec,
            string destinationDirectory,
            CancellationToken cancellationToken)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            var resourceKeyPrefix = string.IsNullOrWhiteSpace(spec.ResourceKeyPrefix)
                ? "MarketPackageDownloader"
                : spec.ResourceKeyPrefix.Trim();
            var packageKind = string.IsNullOrWhiteSpace(spec.PackageKind)
                ? "Market package"
                : spec.PackageKind.Trim();

            if (string.IsNullOrWhiteSpace(spec.PackageUrl))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    resourceKeyPrefix + ".Error.PackageUrlRequired",
                    packageKind + " URL is required."));
            }

            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new ArgumentException("Destination directory cannot be empty.", nameof(destinationDirectory));
            }

            cancellationToken.ThrowIfCancellationRequested();

            Directory.CreateDirectory(destinationDirectory);
            var packageUri = new Uri(spec.PackageUrl.Trim(), UriKind.Absolute);
            var fileName = Path.GetFileName(packageUri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = AppConfigService.NormalizePathSegment(spec.FallbackId) + ".zip";
            }

            var destinationPath = ResolveSafeDestinationPath(destinationDirectory, fileName, resourceKeyPrefix, packageKind);
            try
            {
                await _downloadFileAsync(packageUri, destinationPath, cancellationToken).ConfigureAwait(false);
                ValidateDownloadedPackage(spec, destinationPath, resourceKeyPrefix, packageKind);
                return destinationPath;
            }
            catch
            {
                TryDeleteFile(destinationPath);
                throw;
            }
        }

        internal static string ResolveSafeDestinationPath(
            string destinationDirectory,
            string fileName,
            string resourceKeyPrefix,
            string packageKind)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    resourceKeyPrefix + ".Error.FileNameMissing",
                    packageKind + " file name is missing."));
            }

            if (Path.IsPathRooted(fileName) ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    resourceKeyPrefix + ".Error.FileNameInvalid",
                    packageKind + " file name must not contain a path."));
            }

            var rootPath = Path.GetFullPath(destinationDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidatePath = Path.GetFullPath(Path.Combine(rootPath, fileName));
            if (!candidatePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    resourceKeyPrefix + ".Error.DestinationEscaped",
                    packageKind + " destination escapes the download directory."));
            }

            return candidatePath;
        }

        private static void ValidateDownloadedPackage(
            MarketPackageDownloadSpec spec,
            string path,
            string resourceKeyPrefix,
            string packageKind)
        {
            if (spec.SizeBytes > 0)
            {
                var actualLength = new FileInfo(path).Length;
                if (actualLength != spec.SizeBytes)
                {
                    throw new InvalidOperationException(SharedLocalization.Text(
                        resourceKeyPrefix + ".Error.SizeMismatch",
                        packageKind + " size does not match the market metadata."));
                }
            }

            if (!string.IsNullOrWhiteSpace(spec.Sha256))
            {
                var actualHash = ComputeSha256(path);
                if (!string.Equals(actualHash, spec.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(SharedLocalization.Text(
                        resourceKeyPrefix + ".Error.HashMismatch",
                        packageKind + " SHA256 does not match the market metadata."));
                }
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static async Task DownloadFileAsync(Uri packageUri, string destinationPath, CancellationToken cancellationToken)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new TimeoutWebClient(TimeSpan.FromSeconds(60)))
            using (cancellationToken.Register(client.CancelAsync))
            {
                try
                {
                    await client.DownloadFileTaskAsync(packageUri, destinationPath).ConfigureAwait(false);
                }
                catch (WebException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }
    }
}
