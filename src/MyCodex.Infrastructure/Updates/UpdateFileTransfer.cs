using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Updates
{
    internal static class UpdateFileTransfer
    {
        internal static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

        internal static Task DownloadFileAsync(
            Uri source,
            string path,
            IProgress<UpdateDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            return DownloadFileAsync(source, path, progress, cancellationToken, StallTimeout);
        }

        // WebClient's asynchronous downloads have no timeout, so a server that stops sending would hang forever.
        // A stall surfaces as a WebException, which lets callers fall back to another source.
        internal static async Task DownloadFileAsync(
            Uri source,
            string path,
            IProgress<UpdateDownloadProgress> progress,
            CancellationToken cancellationToken,
            TimeSpan stallTimeout)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var stalled = false;
            using (var client = new WebClient())
            using (var watchdog = new Timer(
                state =>
                {
                    Volatile.Write(ref stalled, true);
                    client.CancelAsync();
                },
                null,
                stallTimeout,
                Timeout.InfiniteTimeSpan))
            using (cancellationToken.Register(client.CancelAsync))
            {
                client.DownloadProgressChanged += (sender, e) =>
                {
                    try
                    {
                        watchdog.Change(stallTimeout, Timeout.InfiniteTimeSpan);
                    }
                    catch (ObjectDisposedException)
                    {
                        // A progress event posted to the UI thread can arrive after the download finished.
                    }

                    progress?.Report(new UpdateDownloadProgress
                    {
                        Percentage = e.ProgressPercentage,
                        BytesReceived = e.BytesReceived,
                        TotalBytesToReceive = e.TotalBytesToReceive
                    });
                };

                try
                {
                    await client.DownloadFileTaskAsync(source, path).ConfigureAwait(false);
                }
                catch (WebException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (WebException error) when (Volatile.Read(ref stalled))
                {
                    throw new WebException(
                        SharedLocalization.Text(
                            "UpdateDownload.Error.Stalled",
                            "The download received no data for too long and was stopped."),
                        error,
                        WebExceptionStatus.Timeout,
                        null);
                }
            }
        }
    }
}
