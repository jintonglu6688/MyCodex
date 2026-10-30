using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Updates;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class UpdateFileTransferTests
    {
        [Test]
        public void StalledDownloadFailsAsTimeoutInsteadOfHanging()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "stalled-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task.Run(async () =>
                {
                    using (var connection = await listener.AcceptTcpClientAsync())
                    {
                        var stream = connection.GetStream();
                        await stream.ReadAsync(new byte[4096], 0, 4096);
                        // Promise a large body, send a few bytes, then stop sending without closing.
                        var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1000000\r\n\r\nstart");
                        await stream.WriteAsync(response, 0, response.Length);
                        await Task.Delay(TimeSpan.FromSeconds(10));
                    }
                });

                var watch = Stopwatch.StartNew();
                var error = Assert.ThrowsAsync<WebException>(() => UpdateFileTransfer.DownloadFileAsync(
                    new Uri("http://127.0.0.1:" + port + "/runtime.tgz"),
                    path,
                    null,
                    CancellationToken.None,
                    TimeSpan.FromMilliseconds(500)));

                Assert.That(error.Status, Is.EqualTo(WebExceptionStatus.Timeout));
                Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(8)));
            }
            finally
            {
                listener.Stop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void CancelledDownloadReportsCancellationNotStall()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "cancelled-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
                {
                    Assert.CatchAsync<OperationCanceledException>(() => UpdateFileTransfer.DownloadFileAsync(
                        new Uri("http://127.0.0.1:" + port + "/runtime.tgz"),
                        path,
                        null,
                        cancellation.Token,
                        TimeSpan.FromSeconds(30)));
                }
            }
            finally
            {
                listener.Stop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
