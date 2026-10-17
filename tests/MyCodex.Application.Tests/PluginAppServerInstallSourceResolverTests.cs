using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PluginAppServerInstallSourceResolverTests
    {
        [Test]
        public void CanInstall_AllowsRemoteMarketplacePluginWithMarketplaceIdAndRemotePluginId()
        {
            var plugin = new AppServerPluginSummary
            {
                MarketplaceName = "OpenAI Curated Remote",
                MarketplaceId = "openai-curated-remote",
                PluginId = "github@openai-curated-remote",
                RemotePluginId = "github",
                SourceType = "remote"
            };

            Assert.That(PluginAppServerInstallSourceResolver.CanInstall(plugin), Is.True);
            Assert.That(PluginAppServerInstallSourceResolver.ResolveRemoteMarketplaceName(plugin), Is.EqualTo("openai-curated-remote"));
            Assert.That(PluginAppServerInstallSourceResolver.ResolveRemotePluginId(plugin), Is.EqualTo("github"));
            Assert.That(PluginAppServerInstallSourceResolver.ResolveUninstallPluginId(plugin), Is.EqualTo("github"));
            Assert.That(
                PluginAppServerInstallSourceResolver.BuildCacheKeys(plugin),
                Is.SupersetOf(new[] { "github@openai-curated-remote", "github" }));
        }

        [Test]
        public void CanInstall_FallsBackToPluginIdSuffixAndNameForRemoteMarketplacePlugin()
        {
            var plugin = new AppServerPluginSummary
            {
                MarketplaceName = "OpenAI Curated Remote",
                PluginId = "app-one@openai-curated-remote",
                Name = "app-one",
                SourceType = "remote"
            };

            Assert.That(PluginAppServerInstallSourceResolver.CanInstall(plugin), Is.True);
            Assert.That(PluginAppServerInstallSourceResolver.ResolveRemoteMarketplaceName(plugin), Is.EqualTo("openai-curated-remote"));
            Assert.That(PluginAppServerInstallSourceResolver.ResolveRemotePluginId(plugin), Is.EqualTo("app-one"));
            Assert.That(PluginAppServerInstallSourceResolver.ResolveUninstallPluginId(plugin), Is.EqualTo("app-one"));
        }

        [Test]
        public void CanInstall_RequiresLocalMarketplacePathAndNameForLocalPlugin()
        {
            var plugin = new AppServerPluginSummary
            {
                MarketplaceName = "Local",
                MarketplacePath = @"E:\Markets\local",
                Name = "github",
                SourceType = "local"
            };

            Assert.That(PluginAppServerInstallSourceResolver.CanInstall(plugin), Is.True);
            Assert.That(PluginAppServerInstallSourceResolver.ResolveUninstallPluginId(plugin), Is.Null);
            plugin.MarketplacePath = null;
            Assert.That(PluginAppServerInstallSourceResolver.CanInstall(plugin), Is.False);
        }

        [Test]
        public void ResolveUninstallPluginId_KeepsFullIdForLocalMarketplacePluginWithAtSuffix()
        {
            var plugin = new AppServerPluginSummary
            {
                MarketplaceName = "Personal",
                MarketplaceId = "personal",
                MarketplacePath = @"C:\Users\user\.agents\plugins\marketplace.json",
                PluginId = "baoyi-store-admin@personal",
                Name = "baoyi-store-admin",
                SourceType = "local"
            };

            Assert.That(PluginAppServerInstallSourceResolver.IsRemoteMarketplacePlugin(plugin), Is.False);
            Assert.That(PluginAppServerInstallSourceResolver.ResolveUninstallPluginId(plugin), Is.EqualTo("baoyi-store-admin@personal"));

            plugin.MarketplacePath = null;
            Assert.That(PluginAppServerInstallSourceResolver.IsRemoteMarketplacePlugin(plugin), Is.False);
            Assert.That(PluginAppServerInstallSourceResolver.ResolveUninstallPluginId(plugin), Is.EqualTo("baoyi-store-admin@personal"));
        }
    }
}
