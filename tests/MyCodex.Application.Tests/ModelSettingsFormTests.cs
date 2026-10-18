using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class ModelSettingsFormTests
    {
        [Test]
        [TestCase("openai/gpt-5.4")]
        [TestCase(null)]
        public void GlobalSettings_OnlyOfferConcreteModelAndEffort(string selectedModel)
        {
            using (var form = new ModelSettingsForm("Model",
                CreateModelOptions("openai/gpt-5.4", "high"), selectedModel, null,
                "Follow Default", "Description", false, true, modelId => true))
            {
                var combos = FormTestDriver.Descendants<ComboBox>(form).ToArray();
                Assert.That(combos[0].Items.Count, Is.EqualTo(1));
                Assert.That(combos[1].Items.Cast<object>().Select(item => item.ToString()),
                    Does.Not.Contain(UiLocalization.Text("Common.Default", "Default")));
                FormTestDriver.ClickOk(form);
                Assert.That(form.SelectedModelId, Is.EqualTo("openai/gpt-5.4"));
                Assert.That(form.SelectedReasoningEffort, Is.EqualTo("high"));
            }
        }

        [Test]
        public void Save_DoesNotPersistReasoningEffort_WhenReasoningIsHidden()
        {
            var models = new List<AppServerModelOption>
            {
                new AppServerModelOption
                {
                    ModelId = "deepseek-v4-pro",
                    DisplayName = "deepseek-v4-pro",
                    DefaultReasoningEffort = "high"
                }
            };

            models[0].ReasoningEfforts.Add(new AppServerModelEffortOption
            {
                EffortId = "high",
                Description = "High"
            });

            using (var form = new ModelSettingsForm(
                "Model",
                models,
                "deepseek-v4-pro",
                "high",
                "Follow Default",
                "Description",
                true,
                false))
            {
                var comboBoxes = FormTestDriver.Descendants<ComboBox>(form).ToArray();
                Assert.That(comboBoxes.Length, Is.EqualTo(2));
                Assert.That(comboBoxes[1].Visible, Is.False);
                Assert.That(comboBoxes[1].TabStop, Is.False);

                FormTestDriver.ClickOk(form);

                Assert.That(form.SelectedModelId, Is.EqualTo("deepseek-v4-pro"));
                Assert.That(form.SelectedReasoningEffort, Is.Null);
            }
        }

        [Test]
        public void Save_PersistsReasoningEffort_WhenSelectedModelSupportsReasoning()
        {
            var models = CreateModelOptions("openai/gpt-5.4", "high");

            using (var form = new ModelSettingsForm(
                "Model",
                models,
                "openai/gpt-5.4",
                "high",
                "Follow Default",
                "Description",
                true,
                true,
                modelId => true))
            {
                FormTestDriver.ClickOk(form);

                Assert.That(form.SelectedModelId, Is.EqualTo("openai/gpt-5.4"));
                Assert.That(form.SelectedReasoningEffort, Is.EqualTo("high"));
            }
        }

        [Test]
        public void Save_DoesNotPersistReasoningEffort_WhenSelectedModelDoesNotSupportReasoning()
        {
            var models = CreateModelOptions("deepseek-v4-pro", "high");

            using (var form = new ModelSettingsForm(
                "Model",
                models,
                "deepseek-v4-pro",
                "high",
                "Follow Default",
                "Description",
                true,
                true,
                modelId => false))
            {
                var comboBoxes = FormTestDriver.Descendants<ComboBox>(form).ToArray();
                Assert.That(comboBoxes.Length, Is.EqualTo(2));
                Assert.That(comboBoxes[1].TabStop, Is.True);
                Assert.That(comboBoxes[1].Enabled, Is.False);

                FormTestDriver.ClickOk(form);

                Assert.That(form.SelectedModelId, Is.EqualTo("deepseek-v4-pro"));
                Assert.That(form.SelectedReasoningEffort, Is.Null);
            }
        }

        [Test]
        public void Save_DisablesUnreportedReasoningCapabilityWithoutCallback()
        {
            var models = new List<AppServerModelOption>
            {
                new AppServerModelOption { ModelId = "official-observed", DisplayName = "Official observed" }
            };

            using (var form = new ModelSettingsForm(
                "Model",
                models,
                "official-observed",
                null,
                "Follow Default",
                "Description"))
            {
                var effortComboBox = FormTestDriver.Descendants<ComboBox>(form).ToArray()[1];
                Assert.That(effortComboBox.Enabled, Is.False);
                Assert.That(effortComboBox.Items, Is.Empty);

                FormTestDriver.ClickOk(form);

                Assert.That(form.SelectedModelId, Is.EqualTo("official-observed"));
                Assert.That(form.SelectedReasoningEffort, Is.Null);
            }
        }

        [Test]
        public void ReasoningChoices_OrderAndLocalizeMaxAndUltra()
        {
            var models = CreateModelOptions("gpt-5.6-sol", "medium");
            models[0].ReasoningEfforts.Clear();
            models[0].ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "xhigh" });
            models[0].ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "ultra" });
            models[0].ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "max" });

            using (var form = new ModelSettingsForm(
                "Model",
                models,
                "gpt-5.6-sol",
                "max",
                "Follow Default",
                "Description",
                true,
                true,
                modelId => true))
            {
                var effortComboBox = FormTestDriver.Descendants<ComboBox>(form).ToArray()[1];

                Assert.That(effortComboBox.Items.Cast<object>().Select(item => item.ToString()).ToArray(),
                    Is.EqualTo(new[]
                    {
                        UiLocalization.Text("Common.Default", "Default"),
                        UiLocalization.Text("ReasoningEffort.Ultra", "Ultra"),
                        UiLocalization.Text("ReasoningEffort.Max", "Max"),
                        UiLocalization.Text("ReasoningEffort.XHigh", "Extra High")
                    }));
            }
        }

        [Test]
        public void Save_UnspecifiedEffortStaysUnspecified()
        {
            using (var form = new ModelSettingsForm("Model", CreateModelOptions("known", "medium"),
                "known", null, "Default", "Description", true, true))
            {
                var combos = FormTestDriver.Descendants<ComboBox>(form).ToArray();
                Assert.That(combos[0].AccessibleName, Is.Not.Empty);
                Assert.That(combos[1].AccessibleName, Is.Not.Empty);
                Assert.That(combos[0].TabIndex, Is.LessThan(combos[1].TabIndex));
                Assert.That(combos[1].SelectedItem.ToString(), Is.EqualTo(UiLocalization.Text("Common.Default", "Default")));
                FormTestDriver.ClickOk(form);
                Assert.That(form.SelectedReasoningEffort, Is.Null);
            }
        }

        [Test]
        public void Save_UnlistedConfiguredModelAndEffortArePreserved()
        {
            using (var form = new ModelSettingsForm("Model", CreateModelOptions("known", "medium"),
                "custom-model", "high", "Default", "Description", true, true, model => model == "known"))
            {
                FormTestDriver.ClickOk(form);
                Assert.That(form.SelectedModelId, Is.EqualTo("custom-model"));
                Assert.That(form.SelectedReasoningEffort, Is.EqualTo("high"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KnownModel_DoesNotAppendUnsupportedPreviousEffort(bool allowDefault)
        {
            using (var form = new ModelSettingsForm("Model", CreateModelOptions("qwen-3.8-max", "low"),
                "qwen-3.8-max", "high", "Default", "Description", allowDefault, true))
            {
                var combos = FormTestDriver.Descendants<ComboBox>(form).ToArray();
                Assert.That(combos[1].AccessibleName, Is.Not.Empty);
                Assert.That(combos[1].TabStop, Is.True);
                Assert.That(combos[0].TabIndex, Is.LessThan(combos[1].TabIndex));
                Assert.That(combos[1].Items.Cast<object>().Select(item => item.ToString()),
                    Does.Not.Contain(UiLocalization.Text("ReasoningEffort.High", "High")));
                FormTestDriver.ClickOk(form);
                Assert.That(form.SelectedReasoningEffort, Is.EqualTo("low"));
            }
        }

        private static List<AppServerModelOption> CreateModelOptions(string modelId, string defaultReasoningEffort)
        {
            var models = new List<AppServerModelOption>
            {
                new AppServerModelOption
                {
                    ModelId = modelId,
                    DisplayName = modelId,
                    DefaultReasoningEffort = defaultReasoningEffort
                }
            };

            models[0].ReasoningEfforts.Add(new AppServerModelEffortOption
            {
                EffortId = defaultReasoningEffort,
                Description = defaultReasoningEffort
            });

            return models;
        }
    }
}
