using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.Markets
{
    public sealed class PluginMarketPackageDownloader
    {
        private readonly MarketPackageDownloader _downloader;

        public PluginMarketPackageDownloader()
            : this(new MarketPackageDownloader())
        {
        }

        internal PluginMarketPackageDownloader(Func<Uri, string, CancellationToken, Task> downloadFileAsync)
            : this(new MarketPackageDownloader(downloadFileAsync))
        {
        }

        private PluginMarketPackageDownloader(MarketPackageDownloader downloader)
        {
            _downloader = downloader ?? new MarketPackageDownloader();
        }

        public Task<string> DownloadAsync(
            PluginMarketEntrySnapshot entry,
            string destinationDirectory,
            CancellationToken cancellationToken)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            return _downloader.DownloadAsync(
                new MarketPackageDownloadSpec
                {
                    PackageUrl = entry.PackageUrl,
                    FallbackId = entry.PluginId,
                    SizeBytes = entry.SizeBytes,
                    Sha256 = entry.Sha256,
                    ResourceKeyPrefix = "PluginMarketPackageDownloader",
                    PackageKind = "Plugin package"
                },
                destinationDirectory,
                cancellationToken);
        }

        internal static string ResolveSafeDestinationPath(string destinationDirectory, string fileName)
        {
            return MarketPackageDownloader.ResolveSafeDestinationPath(
                destinationDirectory,
                fileName,
                "PluginMarketPackageDownloader",
                "Plugin package");
        }
    }
}
