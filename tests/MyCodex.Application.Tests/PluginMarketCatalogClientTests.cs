using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Markets;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class PluginMarketCatalogClientTests
    {
        [Test]
        public void ResolveDefaultIndexUrl_UsesEnvironmentOverrideWhenPresent()
        {
            var url = PluginMarketCatalogClient.ResolveDefaultIndexUrl("  http://127.0.0.1:8765/catalog/index.json  ");

            Assert.That(url, Is.EqualTo("http://127.0.0.1:8765/catalog/index.json"));
        }

        [Test]
        public void ResolveDefaultIndexUrl_FallsBackToDefaultWhenEnvironmentOverrideMissing()
        {
            var url = PluginMarketCatalogClient.ResolveDefaultIndexUrl("   ");

            Assert.That(url, Is.EqualTo("https://market.jtl.world/catalog/index.json"));
        }

        [Test]
        public async Task LoadIndexAsync_ParsesMarketsAndResolvesManifestUrls()
        {
            const string indexUrl = "https://market.example.com/catalog/index.json";
            const string indexJson = "{" +
                "\"markets\":[" +
                "{" +
                "\"id\":\"official\"," +
                "\"displayName\":\"Official Plugins\"," +
                "\"manifestUrl\":\"../markets/official/market.json\"," +
                "\"appServerSource\":\"https://market.example.com/git/official.git\"," +
                "\"detailsUrl\":\"https://market.example.com/official\"" +
                "}," +
                "{" +
                "\"name\":\"community\"," +
                "\"interface\":{\"displayName\":\"Community Plugins\"}," +
                "\"manifestUrl\":\"https://cdn.example.com/community.json\"" +
                "}" +
                "]}";

            var client = new PluginMarketCatalogClient(
                indexUrl,
                (url, cancellationToken) => Task.FromResult(indexJson));

            var snapshot = await client.LoadIndexAsync(CancellationToken.None);

            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.IndexUrl, Is.EqualTo(indexUrl));
            Assert.That(snapshot.Markets, Has.Count.EqualTo(2));

            Assert.That(snapshot.Markets[0].MarketId, Is.EqualTo("official"));
            Assert.That(snapshot.Markets[0].DisplayName, Is.EqualTo("Official Plugins"));
            Assert.That(snapshot.Markets[0].ManifestUrl, Is.EqualTo("https://market.example.com/markets/official/market.json"));
            Assert.That(snapshot.Markets[0].AppServerSource, Is.EqualTo("https://market.example.com/git/official.git"));
            Assert.That(snapshot.Markets[0].DetailsUrl, Is.EqualTo("https://market.example.com/official"));

            Assert.That(snapshot.Markets[1].MarketId, Is.EqualTo("community"));
            Assert.That(snapshot.Markets[1].DisplayName, Is.EqualTo("Community Plugins"));
            Assert.That(snapshot.Markets[1].ManifestUrl, Is.EqualTo("https://cdn.example.com/community.json"));
        }

        [Test]
        public async Task LoadMarketAsync_ParsesPluginsAndResolvesRelativeUrls()
        {
            const string manifestUrl = "https://market.example.com/markets/official/market.json";
            const string manifestJson = "{" +
                "\"name\":\"official\"," +
                "\"appServerSource\":\"https://market.example.com/git/official.git\"," +
                "\"interface\":{\"displayName\":\"Official Plugins\"}," +
                "\"plugins\":[" +
                "{" +
                "\"name\":\"reviewer\"," +
                "\"version\":\"1.2.3\"," +
                "\"description\":\"Review pull requests\"," +
                "\"packageUrl\":\"./packages/reviewer-1.2.3.zip\"," +
                "\"manifestUrl\":\"./plugins/reviewer/plugin.json\"," +
                "\"detailsUrl\":\"./plugins/reviewer\"," +
                "\"sha256\":\"abcdef0123456789\"," +
                "\"size\":12345," +
                "\"policy\":{\"authentication\":\"ON_USE\"}," +
                "\"interface\":{\"displayName\":\"Code Reviewer\",\"category\":\"Engineering\"}" +
                "}" +
                "]}";

            var client = new PluginMarketCatalogClient(
                "https://market.example.com/catalog/index.json",
                (url, cancellationToken) => Task.FromResult(manifestJson));

            var snapshot = await client.LoadMarketAsync(manifestUrl, CancellationToken.None);

            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.MarketId, Is.EqualTo("official"));
            Assert.That(snapshot.DisplayName, Is.EqualTo("Official Plugins"));
            Assert.That(snapshot.ManifestUrl, Is.EqualTo(manifestUrl));
            Assert.That(snapshot.AppServerSource, Is.EqualTo("https://market.example.com/git/official.git"));
            Assert.That(snapshot.Plugins, Has.Count.EqualTo(1));

            var plugin = snapshot.Plugins[0];
            Assert.That(plugin.MarketId, Is.EqualTo("official"));
            Assert.That(plugin.PluginId, Is.EqualTo("reviewer"));
            Assert.That(plugin.DisplayName, Is.EqualTo("Code Reviewer"));
            Assert.That(plugin.Version, Is.EqualTo("1.2.3"));
            Assert.That(plugin.Category, Is.EqualTo("Engineering"));
            Assert.That(plugin.Summary, Is.EqualTo("Review pull requests"));
            Assert.That(plugin.PackageUrl, Is.EqualTo("https://market.example.com/markets/official/packages/reviewer-1.2.3.zip"));
            Assert.That(plugin.ManifestUrl, Is.EqualTo("https://market.example.com/markets/official/plugins/reviewer/plugin.json"));
            Assert.That(plugin.DetailsUrl, Is.EqualTo("https://market.example.com/markets/official/plugins/reviewer"));
            Assert.That(plugin.AuthPolicy, Is.EqualTo("ON_USE"));
            Assert.That(plugin.Sha256, Is.EqualTo("abcdef0123456789"));
            Assert.That(plugin.SizeBytes, Is.EqualTo(12345));
        }
    }
}
