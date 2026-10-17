using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormModelOptionComposerTests
    {
        [Test]
        public void BuildOrderedModelOptions_SortsNewestOpenAiModelsFirstThenVariantRank()
        {
            var models = new[]
            {
                new AppServerModelOption { ModelId = "gpt-5.4-mini" },
                new AppServerModelOption { ModelId = "gpt-5.5" },
                new AppServerModelOption { ModelId = "custom-alpha" },
                new AppServerModelOption { ModelId = "gpt-5.4" }
            };

            var ordered = MainFormModelOptionComposer.BuildOrderedModelOptions(models);

            Assert.That(ordered.Select(item => item.ModelId).ToArray(), Is.EqualTo(new[]
            {
                "gpt-5.5",
                "gpt-5.4",
                "gpt-5.4-mini",
                "custom-alpha"
            }));
        }

        [Test]
        public void GetEffortsForModel_UsesModelEffortsSortedByRankDescending()
        {
            var model = new AppServerModelOption { ModelId = "gpt-5.4", DefaultReasoningEffort = "medium" };
            model.ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "low" });
            model.ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "xhigh" });
            model.ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "medium" });
            model.ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "max" });
            model.ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "ultra" });

            var efforts = MainFormModelOptionComposer.GetEffortsForModel(model);

            Assert.That(efforts.Select(item => item.EffortId).ToArray(),
                Is.EqualTo(new[] { "ultra", "max", "xhigh", "medium", "low" }));
        }

        [Test]
        public void GetEffortsForModel_DoesNotInventUnreportedCapabilities()
        {
            var explicitDefault = MainFormModelOptionComposer.GetEffortsForModel(
                new AppServerModelOption { ModelId = "gpt-5.4", DefaultReasoningEffort = "high" });
            var implicitDefault = MainFormModelOptionComposer.GetEffortsForModel(
                new AppServerModelOption { ModelId = "gpt-5.4" });

            Assert.That(explicitDefault, Is.Empty);
            Assert.That(implicitDefault, Is.Empty);
        }

        [TestCase("gpt-5.4-mini", "5.4 mini")]
        [TestCase("openai/gpt-5.5-xhigh", "5.5 xhigh")]
        [TestCase("custom-model-alpha", "custom model alpha")]
        [TestCase(null, null)]
        public void GetModelVersionLabel_NormalizesLabels(string modelId, string expected)
        {
            var expectedText = expected ?? UiLocalization.Text("Common.Default", "Default");
            Assert.That(MainFormModelOptionComposer.GetModelVersionLabel(modelId), Is.EqualTo(expectedText));
        }

        [Test]
        public void AggregatedModelsAreLabelledByTheirDisplayNameInsteadOfTheProviderKey()
        {
            var aggregated = new AppServerModelOption { ModelId = "ccs-p3a205d/claude-opus-5.5", DisplayName = " Claude Opus 5.5（测试） " };
            var plain = new AppServerModelOption { ModelId = "gpt-5.5", DisplayName = "GPT-5.5" };
            var models = new[] { plain, aggregated };

            Assert.That(MainFormModelOptionComposer.BuildModelMenuLabel(aggregated), Is.EqualTo("Claude Opus 5.5（测试）"));
            Assert.That(MainFormModelOptionComposer.BuildModelMenuLabel(plain), Is.EqualTo("5.5"));
            Assert.That(MainFormModelOptionComposer.GetModelVersionLabel("CCS-p3a205d/claude-opus-5.5", models), Is.EqualTo("Claude Opus 5.5（测试）"));
            Assert.That(MainFormModelOptionComposer.GetModelVersionLabel("gpt-5.5", models), Is.EqualTo("5.5"));
            // A model no longer listed, or one without a display name, keeps the id-based label.
            Assert.That(MainFormModelOptionComposer.GetModelVersionLabel("ccs-gone/model-a", models), Is.EqualTo("ccs gone/model a"));
            Assert.That(MainFormModelOptionComposer.BuildModelMenuLabel(new AppServerModelOption { ModelId = "ccs-k/model-a" }), Is.EqualTo("ccs k/model a"));
            Assert.That(MainFormModelOptionComposer.IsAggregatedModelId("ccs-/model"), Is.False);
            Assert.That(MainFormModelOptionComposer.IsAggregatedModelId("ccs-without-separator"), Is.False);
        }

        [TestCase("minimal", "Minimal", 1)]
        [TestCase("low", "Low", 2)]
        [TestCase("medium", "Medium", 3)]
        [TestCase("high", "High", 4)]
        [TestCase("xhigh", "Extra High", 5)]
        [TestCase("max", "Max", 6)]
        [TestCase("ultra", "Ultra", 7)]
        [TestCase("none", "None", 0)]
        [TestCase("custom", "custom", -1)]
        public void ReasoningEffortTextAndRank_AreStable(string effortId, string expectedText, int expectedRank)
        {
            Assert.That(MainFormModelOptionComposer.GetReasoningEffortText(effortId), Is.EqualTo(GetExpectedReasoningEffortText(effortId, expectedText)));
            Assert.That(MainFormModelOptionComposer.GetReasoningEffortRank(effortId), Is.EqualTo(expectedRank));
        }

        private static string GetExpectedReasoningEffortText(string effortId, string fallback)
        {
            switch ((effortId ?? string.Empty).ToLowerInvariant())
            {
                case "minimal":
                    return UiLocalization.Text("ReasoningEffort.Minimal", fallback);
                case "low":
                    return UiLocalization.Text("ReasoningEffort.Low", fallback);
                case "medium":
                    return UiLocalization.Text("ReasoningEffort.Medium", fallback);
                case "high":
                    return UiLocalization.Text("ReasoningEffort.High", fallback);
                case "xhigh":
                    return UiLocalization.Text("ReasoningEffort.XHigh", fallback);
                case "max":
                    return UiLocalization.Text("ReasoningEffort.Max", fallback);
                case "ultra":
                    return UiLocalization.Text("ReasoningEffort.Ultra", fallback);
                case "none":
                    return UiLocalization.Text("ReasoningEffort.None", fallback);
                default:
                    return fallback;
            }
        }
    }
}
