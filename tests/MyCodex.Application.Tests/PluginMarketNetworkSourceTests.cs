using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.SourceBehavior)]
    [TestFixture]
    public sealed class PluginMarketNetworkSourceTests
    {
        [Test]
        public void PluginMarketNetworkDownloads_UseTimeoutWebClient()
        {
            var catalogClient = RepositorySource.ReadSourceFile("PluginMarketCatalogClient.cs");
            var timeoutWebClient = RepositorySource.ReadSourceFile("TimeoutWebClient.cs");

            Assert.That(catalogClient, Does.Contain("new TimeoutWebClient(TimeSpan.FromSeconds(30))"));
            Assert.That(timeoutWebClient, Does.Contain("request.Timeout = _timeoutMilliseconds"));
            Assert.That(timeoutWebClient, Does.Contain("httpRequest.ReadWriteTimeout = _timeoutMilliseconds"));
        }
    }
}
