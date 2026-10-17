using System;
using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerModelProviderCapabilitiesMapperTests
    {
        [Test]
        public void BuildCapabilities_ParsesBooleanFields()
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "imageGeneration", true },
                { "namespaceTools", false },
                { "webSearch", true }
            };

            var capabilities = AppServerModelProviderCapabilitiesMapper.BuildCapabilities(result);

            Assert.That(capabilities.ImageGeneration, Is.True);
            Assert.That(capabilities.NamespaceTools, Is.False);
            Assert.That(capabilities.WebSearch, Is.True);
        }

        [Test]
        public void BuildCapabilities_ToleratesMissingAndStringBooleanFields()
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "imageGeneration", "true" },
                { "namespaceTools", "not-a-bool" }
            };

            var capabilities = AppServerModelProviderCapabilitiesMapper.BuildCapabilities(result);

            Assert.That(capabilities.ImageGeneration, Is.True);
            Assert.That(capabilities.NamespaceTools, Is.False);
            Assert.That(capabilities.WebSearch, Is.False);
        }
    }
}
