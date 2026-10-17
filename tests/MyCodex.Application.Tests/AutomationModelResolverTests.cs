using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationModelResolverTests
    {
        [Test]
        public async Task ResolveAsync_UsesFreshOfficialSettingsWithoutCatalogFallback()
        {
            var resolver = new AutomationModelResolver(null, () => "stale", () => "medium", model => false,
                definition => Task.FromResult(new SessionDefaultsResolution
                {
                    ModelId = "configured-unlisted", ReasoningEffort = "high"
                }));

            var resolution = await resolver.ResolveAsync(new AutomationDefinitionSnapshot());

            Assert.That(resolution.ModelId, Is.EqualTo("configured-unlisted"));
            Assert.That(resolution.ReasoningEffort, Is.EqualTo("high"));
            Assert.That(resolution.UsedFallback, Is.False);
        }

        [Test]
        public async Task ResolveAsync_UsesConfiguredModelWhenAvailable()
        {
            var resolver = new AutomationModelResolver(
                () => Task.FromResult<IReadOnlyList<AppServerModelOption>>(new[]
                {
                    CreateModel("gpt-5.5", "high")
                }),
                () => "gpt-5.5",
                () => "high",
                modelId => true);

            var resolution = await resolver.ResolveAsync(new AutomationDefinitionSnapshot
            {
                ModelId = "gpt-5.5",
                ReasoningEffort = "high"
            });

            Assert.That(resolution.ModelId, Is.EqualTo("gpt-5.5"));
            Assert.That(resolution.ReasoningEffort, Is.EqualTo("high"));
            Assert.That(resolution.UsedFallback, Is.False);
            Assert.That(resolution.Message, Is.Null);
        }

        [Test]
        public async Task ResolveAsync_FallsBackToCurrentDefaultModelWhenConfiguredModelIsMissing()
        {
            var resolver = new AutomationModelResolver(
                () => Task.FromResult<IReadOnlyList<AppServerModelOption>>(new[]
                {
                    CreateModel("deepseek-chat", "medium"),
                    CreateModel("claude-sonnet", "medium")
                }),
                () => "deepseek-chat",
                () => "medium",
                modelId => modelId != null && modelId.StartsWith("gpt-"));

            var resolution = await resolver.ResolveAsync(new AutomationDefinitionSnapshot
            {
                ModelId = "gpt-5.5",
                ReasoningEffort = "high"
            });

            Assert.That(resolution.ConfiguredModelId, Is.EqualTo("gpt-5.5"));
            Assert.That(resolution.ConfiguredReasoningEffort, Is.EqualTo("high"));
            Assert.That(resolution.ModelId, Is.EqualTo("deepseek-chat"));
            Assert.That(resolution.ReasoningEffort, Is.Null);
            Assert.That(resolution.UsedFallback, Is.True);
            Assert.That(resolution.Message, Does.Contain("gpt-5.5"));
            Assert.That(resolution.Message, Does.Contain("deepseek-chat"));
        }

        [Test]
        public async Task ResolveAsync_FallsBackToFirstAvailableModelWhenDefaultIsUnavailable()
        {
            var resolver = new AutomationModelResolver(
                () => Task.FromResult<IReadOnlyList<AppServerModelOption>>(new[]
                {
                    CreateModel("claude-sonnet", "medium"),
                    CreateModel("deepseek-chat", "medium")
                }),
                () => "missing-default",
                () => "medium",
                modelId => false);

            var resolution = await resolver.ResolveAsync(new AutomationDefinitionSnapshot
            {
                ModelId = "gpt-5.5",
                ReasoningEffort = "high"
            });

            Assert.That(resolution.ModelId, Is.EqualTo("claude-sonnet"));
            Assert.That(resolution.ReasoningEffort, Is.Null);
            Assert.That(resolution.UsedFallback, Is.True);
        }

        private static AppServerModelOption CreateModel(string modelId, string defaultReasoningEffort)
        {
            var model = new AppServerModelOption
            {
                ModelId = modelId,
                DisplayName = modelId,
                DefaultReasoningEffort = defaultReasoningEffort
            };
            model.ReasoningEfforts.Add(new AppServerModelEffortOption
            {
                EffortId = defaultReasoningEffort,
                Description = defaultReasoningEffort
            });
            return model;
        }
    }
}
