using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PluginUninstallWorkflowTests
    {
        [Test]
        public void ExecuteAsync_RuntimeFailureDoesNotRemoveTrackedPlugin()
        {
            var expected = new InvalidOperationException("remote uninstall failed");
            var removed = false;

            var actual = Assert.ThrowsAsync<InvalidOperationException>(() =>
                PluginUninstallWorkflow.ExecuteAsync(
                    () => Task.FromException(expected),
                    () => removed = true));

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(removed, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_RuntimeSuccessRemovesTrackedPluginAfterward()
        {
            var sequence = new List<string>();

            await PluginUninstallWorkflow.ExecuteAsync(
                () =>
                {
                    sequence.Add("runtime");
                    return Task.CompletedTask;
                },
                () => sequence.Add("cache"));

            Assert.That(sequence, Is.EqualTo(new[] { "runtime", "cache" }));
        }

        [Test]
        public void ExecuteAsync_MissingRuntimeOperationFailsBeforeTrackedCleanup()
        {
            var removed = false;

            Assert.ThrowsAsync<ArgumentNullException>(() =>
                PluginUninstallWorkflow.ExecuteAsync(null, () => removed = true));

            Assert.That(removed, Is.False);
        }

        [Test]
        public void IsStillInstalled_MatchesRemotePluginAcrossRuntimeAliases()
        {
            var target = RemotePlugin(
                "openai-templates@openai-curated-remote",
                "plugin_connector_templates",
                true);
            var refreshed = RemotePlugin(
                "openai-templates",
                "plugin_connector_templates",
                true);

            Assert.That(
                PluginUninstallWorkflow.IsStillInstalled(target, new[] { refreshed }),
                Is.True);
        }

        [Test]
        public void IsStillInstalled_IgnoresMatchingPluginWhenRefreshedStateIsUninstalled()
        {
            var target = RemotePlugin(
                "openai-templates@openai-curated-remote",
                "plugin_connector_templates",
                true);
            var refreshed = RemotePlugin(
                "openai-templates@openai-curated-remote",
                "plugin_connector_templates",
                false);

            Assert.That(
                PluginUninstallWorkflow.IsStillInstalled(target, new[] { refreshed }),
                Is.False);
        }

        [Test]
        public void IsStillInstalled_DoesNotMatchSameNameFromDifferentQualifiedMarketplace()
        {
            var target = new AppServerPluginSummary
            {
                PluginId = "templates@market-one",
                Name = "templates",
                Installed = true,
                SourceType = "local"
            };
            var refreshed = new AppServerPluginSummary
            {
                PluginId = "templates@market-two",
                Name = "templates",
                Installed = true,
                SourceType = "local"
            };

            Assert.That(
                PluginUninstallWorkflow.IsStillInstalled(target, new[] { refreshed }),
                Is.False);
        }

        [Test]
        public void StillInstalledMessage_DistinguishesImplicitDefaultPolicy()
        {
            var ordinary = RemotePlugin(
                "openai-templates@openai-curated-remote",
                "plugin_connector_templates",
                true);
            var defaultManaged = RemotePlugin(
                "openai-templates@openai-curated-remote",
                "plugin_connector_templates",
                true);
            defaultManaged.InstallPolicySource = "IMPLICIT_CANONICAL_APP";

            var ordinaryMessage = PluginUninstallStatusMessageComposer.BuildStillInstalledMessage(ordinary);
            var defaultManagedMessage = PluginUninstallStatusMessageComposer.BuildStillInstalledMessage(defaultManaged);

            Assert.That(ordinaryMessage, Does.Contain("Default templates"));
            Assert.That(defaultManagedMessage, Does.Contain("Default templates"));
            Assert.That(defaultManagedMessage, Is.Not.EqualTo(ordinaryMessage));
        }

        private static AppServerPluginSummary RemotePlugin(
            string pluginId,
            string remotePluginId,
            bool installed)
        {
            return new AppServerPluginSummary
            {
                MarketplaceId = "openai-curated-remote",
                PluginId = pluginId,
                RemotePluginId = remotePluginId,
                Name = "openai-templates",
                DisplayName = "Default templates",
                SourceType = "remote",
                Installed = installed
            };
        }
    }
}
