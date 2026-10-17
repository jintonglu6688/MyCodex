using System;
using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerExperimentalFeatureMapperTests
    {
        [Test]
        public void BuildFeatures_ParsesOfficialFeatureListShape()
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "data",
                    new object[]
                    {
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "name", "browser_use" },
                            { "stage", "beta" },
                            { "enabled", true },
                            { "defaultEnabled", false },
                            { "displayName", "Browser Use" },
                            { "description", "Use an in-app browser." },
                            { "announcement", "New browser automation capability." }
                        }
                    }
                },
                { "nextCursor", "cursor-2" }
            };

            var features = AppServerExperimentalFeatureMapper.BuildFeatures(result);

            Assert.That(features, Has.Count.EqualTo(1));
            Assert.That(features[0].Name, Is.EqualTo("browser_use"));
            Assert.That(features[0].Stage, Is.EqualTo("beta"));
            Assert.That(features[0].Enabled, Is.True);
            Assert.That(features[0].DefaultEnabled, Is.False);
            Assert.That(features[0].DisplayName, Is.EqualTo("Browser Use"));
            Assert.That(features[0].Description, Is.EqualTo("Use an in-app browser."));
            Assert.That(features[0].Announcement, Is.EqualTo("New browser automation capability."));
            Assert.That(AppServerExperimentalFeatureMapper.GetNextCursor(result), Is.EqualTo("cursor-2"));
        }

        [Test]
        public void BuildFeatures_ToleratesMissingOptionalFieldsAndSorts()
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "data",
                    new object[]
                    {
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "name", "zeta" },
                            { "enabled", "true" },
                            { "defaultEnabled", "false" }
                        },
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "name", "alpha" }
                        },
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "stage", "beta" }
                        }
                    }
                }
            };

            var features = AppServerExperimentalFeatureMapper.BuildFeatures(result);

            Assert.That(features, Has.Count.EqualTo(2));
            Assert.That(features[0].Name, Is.EqualTo("alpha"));
            Assert.That(features[0].Enabled, Is.False);
            Assert.That(features[1].Name, Is.EqualTo("zeta"));
            Assert.That(features[1].Enabled, Is.True);
            Assert.That(features[1].DefaultEnabled, Is.False);
        }
    }
}
