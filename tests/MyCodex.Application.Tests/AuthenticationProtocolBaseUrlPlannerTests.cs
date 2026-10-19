using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationProtocolBaseUrlPlannerTests
    {
        [TestCase("https://api.deepseek.com", "anthropic", "https://api.deepseek.com/anthropic")]
        [TestCase(" https://api.deepseek.com/ ", "anthropic", "https://api.deepseek.com/anthropic")]
        [TestCase("https://api.deepseek.com/anthropic", "responses", "https://api.deepseek.com")]
        [TestCase("https://api.deepseek.com/anthropic", "chat_completions", "https://api.deepseek.com")]
        [TestCase("https://api.deepseek.com", "chat_completions", null)]
        [TestCase("https://api.deepseek.com/anthropic", "anthropic", null)]
        [TestCase("https://example.test/v1", "anthropic", null)]
        [TestCase("", "anthropic", null)]
        public void PresetEndpointFollowsSelectedFormat(string baseUrl, string kind, string expected)
        {
            Assert.That(AuthenticationProtocolBaseUrlPlanner.Resolve(baseUrl, kind, null, new[] { DeepSeek() }), Is.EqualTo(expected));
        }

        [Test]
        public void TemplateTakesPriorityOverOtherPresetsWithTheSameEndpoint()
        {
            var other = Preset("https://api.deepseek.com", "https://other.test/anthropic");
            Assert.That(AuthenticationProtocolBaseUrlPlanner.Resolve("https://api.deepseek.com", "anthropic", DeepSeek(), new[] { other }),
                Is.EqualTo("https://api.deepseek.com/anthropic"));
            Assert.That(AuthenticationProtocolBaseUrlPlanner.Resolve("https://api.deepseek.com", "anthropic", null, new[] { DeepSeek(), other }),
                Is.Null, "Presets that disagree leave the URL unchanged.");
        }

        [Test]
        public void PresetsWithoutTheSelectedFormatLeaveTheUrlUnchanged()
        {
            var shared = Preset("https://api.deepseek.com", null);
            Assert.That(AuthenticationProtocolBaseUrlPlanner.Resolve("https://api.deepseek.com", "anthropic", null, new[] { DeepSeek(), shared }), Is.Null);
            Assert.That(AuthenticationProtocolBaseUrlPlanner.Resolve("https://api.deepseek.com", "anthropic", shared, new[] { DeepSeek() }), Is.Null);
            Assert.That(AuthenticationProtocolBaseUrlPlanner.Resolve("https://api.deepseek.com", "anthropic", null,
                new[] { new AuthenticationProviderPreset { BaseUrl = "https://api.deepseek.com", ProtocolBaseUrls = null } }), Is.Null);
        }

        [Test]
        public void PresetsSharingAnEndpointAgreeOnTheTarget()
        {
            Assert.That(AuthenticationProtocolBaseUrlPlanner.Resolve("https://api.deepseek.com", "anthropic", null, new[] { DeepSeek(), DeepSeek() }),
                Is.EqualTo("https://api.deepseek.com/anthropic"));
        }

        private static AuthenticationProviderPreset DeepSeek()
        {
            return Preset("https://api.deepseek.com", "https://api.deepseek.com/anthropic");
        }

        private static AuthenticationProviderPreset Preset(string openAi, string anthropic)
        {
            var urls = new Dictionary<string, string> { { "responses", openAi }, { "chat_completions", openAi } };
            if (anthropic != null) urls["anthropic"] = anthropic;
            return new AuthenticationProviderPreset { Name = "DeepSeek", Kind = "responses", BaseUrl = openAi, ProtocolBaseUrls = urls };
        }
    }
}
