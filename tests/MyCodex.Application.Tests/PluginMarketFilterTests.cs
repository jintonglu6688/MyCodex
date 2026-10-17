using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class PluginMarketFilterTests
    {
        [Test]
        public void Apply_WhenMarketAndKeywordProvided_ReturnsOnlyMatchingItems()
        {
            var items = new[]
            {
                CreateItem("official", "terminal-tools", "Terminal Tools", "CLI helpers"),
                CreateItem("official", "git-tools", "Git Tools", "Repository status"),
                CreateItem("community", "terminal-community", "Terminal Community", "CLI helpers")
            };

            var result = PluginMarketFilter.Apply(items, "official", "terminal").ToList();

            Assert.That(result.Select(item => item.PluginId), Is.EqualTo(new[] { "terminal-tools" }));
        }

        [Test]
        public void Apply_WhenKeywordIsEmpty_ReturnsItemsMatchingMarketOnly()
        {
            var items = new[]
            {
                CreateItem("official", "official-one", "Official One", "alpha"),
                CreateItem("community", "community-one", "Community One", "beta"),
                CreateItem("official", "official-two", "Official Two", "gamma")
            };

            var result = PluginMarketFilter.Apply(items, "official", string.Empty).ToList();

            Assert.That(result.Select(item => item.PluginId), Is.EqualTo(new[] { "official-one", "official-two" }));
        }

        [Test]
        public void Apply_WhenMarketIsEmpty_FiltersByKeywordOnly()
        {
            var items = new[]
            {
                CreateItem("official", "official-one", "Official One", "alpha"),
                CreateItem("community", "community-one", "Community One", "beta keyword"),
                CreateItem("community", "community-two", "Another Plugin", "gamma")
            };

            var result = PluginMarketFilter.Apply(items, null, "keyword").ToList();

            Assert.That(result.Select(item => item.PluginId), Is.EqualTo(new[] { "community-one" }));
        }

        [Test]
        public void Apply_WhenCategoryProvided_ReturnsOnlyMatchingCategory()
        {
            var items = new[]
            {
                CreateItem("OpenAI Curated Remote", "docs", "Documents", "docs", "Productivity"),
                CreateItem("OpenAI Curated Remote", "github", "GitHub", "repos", "Developer Tools"),
                CreateItem("MyCodex Official", "tools", "Tools", "tools", "Developer Tools")
            };

            var result = PluginMarketFilter.Apply(items, "OpenAI Curated Remote", "git", "Developer Tools").ToList();

            Assert.That(result.Select(item => item.PluginId), Is.EqualTo(new[] { "github" }));
        }

        [Test]
        public void Apply_WhenItemsAreNull_ReturnsEmpty()
        {
            var result = PluginMarketFilter.Apply(null, "official", "keyword");

            Assert.That(result, Is.Empty);
        }

        private static PluginMarketListItem CreateItem(
            string marketName,
            string pluginId,
            string displayName,
            string summary,
            string category = null)
        {
            return new PluginMarketListItem
            {
                MarketName = marketName,
                PluginId = pluginId,
                DisplayName = displayName,
                Summary = summary,
                IsRemoteCatalog = true,
                AppServerPlugin = new AppServerPluginSummary
                {
                    MarketplaceName = marketName,
                    PluginId = pluginId,
                    DisplayName = displayName,
                    ShortDescription = summary
                },
                RemoteEntry = new PluginMarketEntrySnapshot
                {
                    MarketId = marketName,
                    PluginId = pluginId,
                    DisplayName = displayName,
                    Summary = summary,
                    Category = category
                }
            };
        }
    }
}
