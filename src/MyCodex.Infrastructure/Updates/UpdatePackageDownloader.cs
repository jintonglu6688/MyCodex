using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Updates
{
    public sealed class UpdatePackageDownloader
    {
        public async Task<string> DownloadVerifiedAsync(
            Uri sourceUri,
            UpdateAssetReference asset,
            string destinationDirectory,
            CancellationToken cancellationToken)
        {
            return await DownloadVerifiedAsync(
                sourceUri,
                asset,
                destinationDirectory,
                null,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> DownloadVerifiedAsync(
            Uri sourceUri,
            UpdateAssetReference asset,
            string destinationDirectory,
            IProgress<UpdateDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            if (sourceUri == null)
            {
                throw new ArgumentNullException(nameof(sourceUri));
            }

            UpdateManifestClient.EnsureTrustedUpdateUri(sourceUri);
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new ArgumentException("Destination directory cannot be empty.", nameof(destinationDirectory));
            }

            Directory.CreateDirectory(destinationDirectory);
            var fileName = !string.IsNullOrWhiteSpace(asset.FileName)
                ? asset.FileName.Trim()
                : Path.GetFileName(sourceUri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "UpdatePackage.Error.FileNameMissing",
                    "Update asset file name is missing."));
            }

            var destinationPath = ResolveSafeDestinationPath(destinationDirectory, fileName);
            try
            {
                await UpdateFileTransfer.DownloadFileAsync(sourceUri, destinationPath, progress, cancellationToken).ConfigureAwait(false);
                VerifySize(destinationPath, asset.Size);
                VerifySha256(destinationPath, asset.Sha256);
                return destinationPath;
            }
            catch
            {
                TryDeleteFile(destinationPath);
                throw;
            }
        }

        public static string ComputeSha256Hex(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string ResolveSafeDestinationPath(string destinationDirectory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "UpdatePackage.Error.FileNameMissing",
                    "Update asset file name is missing."));
            }

            if (Path.IsPathRooted(fileName) ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "UpdatePackage.Error.FileNameInvalid",
                    "Update asset file name must not contain a path."));
            }

            var root = Path.GetFullPath(destinationDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(root, fileName));

            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "UpdatePackage.Error.DestinationEscaped",
                    "Update asset destination escapes the download directory."));
            }

            return candidate;
        }

        private static void VerifySize(string filePath, long? expectedSize)
        {
            if (!expectedSize.HasValue)
            {
                return;
            }

            var actualSize = new FileInfo(filePath).Length;
            if (actualSize != expectedSize.Value)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.CurrentCulture,
                    SharedLocalization.Text(
                        "UpdatePackage.Error.SizeMismatch.Template",
                        "Update asset size mismatch. Expected {0} bytes, got {1} bytes."),
                    expectedSize.Value,
                    actualSize));
            }
        }

        private static void VerifySha256(string filePath, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "UpdatePackage.Error.Sha256Missing",
                    "Update asset SHA256 is missing."));
            }

            var actual = ComputeSha256Hex(filePath);
            if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "UpdatePackage.Error.Sha256Mismatch",
                    "Update asset SHA256 verification failed."));
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
    }
}
