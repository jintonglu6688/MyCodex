using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ModelMetadataResolverTests
    {
        // Shape of https://models.dev/api.json, reduced to the fields used here.
        internal const string CatalogJson = @"{
  ""openai"": { ""api"": ""https://api.openai.com/v1"", ""models"": {
    ""gpt-5.5"": { ""id"": ""gpt-5.5"", ""limit"": { ""context"": 1050000 },
      ""reasoning_options"": [ { ""type"": ""effort"", ""values"": [ ""none"", ""low"", ""medium"", ""high"", ""xhigh"" ] } ] },
    ""gpt-6-sol"": { ""id"": ""gpt-6-sol"", ""limit"": { ""context"": 1050000 },
      ""reasoning_options"": [ { ""type"": ""toggle"" }, { ""type"": ""effort"", ""values"": [ ""MAX"", ""low"", ""default"", ""high"" ] } ] },
    ""gpt-image-2"": { ""id"": ""gpt-image-2"", ""limit"": { ""context"": 0 } },
    ""twin"": { ""limit"": { ""context"": 2000 } } } },
  ""openrouter"": { ""api"": ""https://openrouter.ai/api/v1"", ""models"": {
    ""openai/gpt-5.5"": { ""canonical_model_id"": ""openai/gpt-5.5"", ""limit"": { ""context"": 400000 } },
    ""openai/gpt-6-sol"": { ""canonical_model_id"": ""openai/gpt-6-sol"" },
    ""moonshotai/kimi-k3"": { ""canonical_model_id"": ""moonshotai/kimi-k3"" },
    ""shared/twin"": { ""canonical_model_id"": ""openai/twin"" },
    ""other/twin"": { ""canonical_model_id"": ""moonshotai/twin"" } } },
  ""moonshotai"": { ""api"": ""https://api.moonshot.cn/v1"", ""models"": {
    ""kimi-k3"": { ""limit"": { ""context"": 262144 }, ""reasoning_options"": [ { ""type"": ""effort"", ""values"": [ ""low"", ""medium"", ""high"" ] } ] },
    ""twin"": { ""limit"": { ""context"": 1000 } } } },
  ""zai-coding"": { ""api"": ""https://api.z.ai/api/coding/paas/v4"", ""models"": {
    ""glm-coding"": { ""limit"": { ""context"": 200000 } } } }
}";

        internal static ModelMetadataCatalog Catalog()
        {
            return ModelsDevCatalogSource.ToCatalog(ModelsDevCatalogSource.Parse(CatalogJson));
        }

        [Test]
        public void RelayModelUsesMakerEntryAndCodexLevelOrder()
        {
            var resolver = new ModelMetadataResolver("https://codex.zxrjy.net:3500/", null, Catalog());

            var gpt55 = resolver.Resolve("gpt-5.5");
            Assert.That(gpt55.ContextWindow, Is.EqualTo(1050000));
            Assert.That(gpt55.Sources, Is.EqualTo(ModelMetadataSources.ModelsDev));
            var row = new AuthenticationCatalogModel { Model = "gpt-5.5" };
            Assert.That(ModelMetadataResolver.FillMissing(row, gpt55), Is.True);
            Assert.That(row.ReasoningLevels, Is.EqualTo(new[] { "none", "low", "medium", "high", "xhigh" }));

            var sol = new AuthenticationCatalogModel { Model = "gpt-6-sol" };
            Assert.That(resolver.FillMissing(sol), Is.EqualTo(ModelMetadataSources.ModelsDev));
            Assert.That(sol.ReasoningLevels, Is.EqualTo(new[] { "low", "high", "max" }), "Unknown values are dropped and levels follow Codex order.");

            Assert.That(resolver.Resolve("gpt-image-2"), Is.Null, "A zero context window is not data.");
            Assert.That(resolver.Resolve("gpt-image-2.5-flare"), Is.Null);
        }

        [Test]
        public void ProviderIdsAreNormalizedForMakerLookup()
        {
            var resolver = new ModelMetadataResolver("relay.example", null, Catalog());

            Assert.That(resolver.Resolve("someone/OpenAI/GPT-5.5:free")?.ContextWindow, Is.EqualTo(1050000));
            Assert.That(resolver.Resolve("gpt-5.5[1m]")?.ContextWindow, Is.EqualTo(1050000));
        }

        [Test]
        public void SameEndpointProviderWinsAndMakerFillsRemainingFields()
        {
            var resolver = new ModelMetadataResolver("https://openrouter.ai/api/v1/chat/completions", null, Catalog());

            var match = resolver.Resolve("openai/gpt-5.5");
            Assert.That(match.ContextWindow, Is.EqualTo(400000), "The recognized provider's own window wins.");
            Assert.That(match.ReasoningEfforts, Is.EqualTo(new[] { "none", "low", "medium", "high", "xhigh" }));
        }

        [Test]
        public void PresetForSameEndpointHasPriority()
        {
            var presets = new[]
            {
                new AuthenticationProviderPreset { BaseUrl = "https://API.moonshot.cn/v1/", Models = new List<AuthenticationCatalogModel>
                {
                    new AuthenticationCatalogModel { Model = "kimi-k3", ContextWindow = 1048576 }
                } },
                new AuthenticationProviderPreset { BaseUrl = "https://elsewhere.example/v1", Models = new List<AuthenticationCatalogModel>
                {
                    new AuthenticationCatalogModel { Model = "kimi-k3", ContextWindow = 1, ReasoningLevels = new List<string> { "ultra" } }
                } }
            };
            var resolver = new ModelMetadataResolver("https://api.moonshot.cn/v1/responses", presets, Catalog());

            var match = resolver.Resolve("Kimi-K3");
            Assert.That(match.ContextWindow, Is.EqualTo(1048576));
            Assert.That(match.ReasoningEfforts, Is.EqualTo(new[] { "low", "medium", "high" }));
            Assert.That(match.Sources, Is.EqualTo(ModelMetadataSources.Preset | ModelMetadataSources.ModelsDev));

            var presetOnly = new ModelMetadataResolver("https://api.moonshot.cn/v1", presets, null).Resolve("kimi-k3");
            Assert.That(presetOnly.Sources, Is.EqualTo(ModelMetadataSources.Preset));
        }

        [Test]
        public void DifferentPlanPathOnSameHostIsNotTheSameEndpoint()
        {
            Assert.That(new ModelMetadataResolver("https://api.z.ai/api/coding/paas/v4", null, Catalog()).Resolve("glm-coding")?.ContextWindow, Is.EqualTo(200000));
            Assert.That(new ModelMetadataResolver("https://api.z.ai/api/paas/v4", null, Catalog()).Resolve("glm-coding"), Is.Null);
        }

        [Test]
        public void AmbiguousMakersYieldNoSuggestion()
        {
            Assert.That(new ModelMetadataResolver("relay.example", null, Catalog()).Resolve("twin"), Is.Null);
        }

        [Test]
        public void FillOnlyCompletesBlankFields()
        {
            var match = new ModelMetadataResolver("relay.example", null, Catalog()).Resolve("gpt-5.5");

            var kept = new AuthenticationCatalogModel { Model = "gpt-5.5", ContextWindow = 128000, ReasoningLevels = new List<string> { "high" } };
            Assert.That(ModelMetadataResolver.FillMissing(kept, match), Is.False);
            Assert.That(kept.ContextWindow, Is.EqualTo(128000));
            Assert.That(kept.ReasoningLevels, Is.EqualTo(new[] { "high" }));

            var emptyLevels = new AuthenticationCatalogModel { Model = "gpt-5.5", ContextWindow = 128000, ReasoningLevels = new List<string>() };
            Assert.That(ModelMetadataResolver.FillMissing(emptyLevels, match), Is.True);
            Assert.That(emptyLevels.ContextWindow, Is.EqualTo(128000));
            Assert.That(emptyLevels.ReasoningLevels, Has.Count.EqualTo(5));

            var invalidDefault = new AuthenticationCatalogModel { Model = "gpt-5.5", DefaultReasoningLevel = "ultra" };
            Assert.That(ModelMetadataResolver.FillMissing(invalidDefault, match), Is.True);
            Assert.That(invalidDefault.ContextWindow, Is.EqualTo(1050000));
            Assert.That(invalidDefault.ReasoningLevels, Is.Null, "Levels must keep the saved default valid.");
        }
    }
}
