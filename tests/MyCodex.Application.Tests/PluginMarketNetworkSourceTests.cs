using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PluginMarketNetworkSourceTests
    {
        [Test]
        public void PluginMarketNetworkDownloads_UseTimeoutWebClient()
        {
            var catalogClient = File.ReadAllText(GetSourcePath("PluginMarketCatalogClient.cs"));
            var packageDownloader = File.ReadAllText(GetSourcePath("MarketPackageDownloader.cs"));
            var timeoutWebClient = File.ReadAllText(GetSourcePath("TimeoutWebClient.cs"));

            Assert.That(catalogClient, Does.Contain("new TimeoutWebClient(TimeSpan.FromSeconds(30))"));
            Assert.That(packageDownloader, Does.Contain("new TimeoutWebClient(TimeSpan.FromSeconds(60))"));
            Assert.That(timeoutWebClient, Does.Contain("request.Timeout = _timeoutMilliseconds"));
            Assert.That(timeoutWebClient, Does.Contain("httpRequest.ReadWriteTimeout = _timeoutMilliseconds"));
        }

        private static string GetSourcePath(string fileName)
        {
            var path = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "src",
                "MyCodex.Infrastructure",
                "Markets",
                fileName);
            return Path.GetFullPath(path);
        }
    }
}
