using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class OfficialCodexSettingsFormTests
    {
        [Test]
        public void Form_UsesAccessibleControlsAndPredictableTabOrder()
        {
            using (var form = new OfficialCodexSettingsForm(new AppServerConfigSnapshot()))
            {
                var reasoning = GetField<ComboBox>(form, "_reasoningSummaryComboBox");
                var fastMode = GetField<CheckBox>(form, "_serviceTierCheckBox");
                var description = GetField<TextBox>(form, "_descriptionTextBox");

                Assert.That(reasoning.AccessibleName, Is.EqualTo(UiLocalization.Text("OfficialCodexSettingsForm.Accessible.ReasoningSummary", "Reasoning summary")));
                Assert.That(reasoning.TabIndex, Is.Zero);
                Assert.That(fastMode.AccessibleName, Is.EqualTo(UiLocalization.Text("OfficialCodexSettingsForm.Accessible.ServiceTier", "Fast mode")));
                Assert.That(fastMode.TabIndex, Is.EqualTo(2));
                Assert.That(fastMode.ThreeState, Is.False);
                Assert.That(description.ReadOnly, Is.True);
                Assert.That(description.TabIndex, Is.EqualTo(8));
            }
        }

        [TestCase("priority")]
        [TestCase("fast")]
        public void Form_LoadsCurrentAndLegacyFastServiceTier(string serviceTier)
        {
            var snapshot = new AppServerConfigSnapshot { ServiceTier = serviceTier };
            snapshot.Origins["service_tier"] = new AppServerConfigOrigin { SourceType = "user" };

            using (var form = new OfficialCodexSettingsForm(snapshot))
            {
                InvokeApplySelection(form);

                Assert.That(GetField<CheckBox>(form, "_serviceTierCheckBox").Checked, Is.True);
                Assert.That(form.ServiceTier, Is.EqualTo("priority"));
            }
        }

        [Test]
        public void Form_OffersIndexedWebSearch()
        {
            using (var form = new OfficialCodexSettingsForm(new AppServerConfigSnapshot()))
            {
                var comboBox = GetField<ComboBox>(form, "_webSearchModeComboBox");
                var indexed = comboBox.Items.Cast<object>().First(item =>
                    string.Equals(GetChoiceValue(item), "indexed", System.StringComparison.Ordinal));
                comboBox.SelectedItem = indexed;

                InvokeApplySelection(form);

                Assert.That(form.WebSearchMode, Is.EqualTo("indexed"));
            }
        }

        [Test]
        public void Form_PreservesExistingCustomValuesWithoutSilentlyRoundingOrClearing()
        {
            var snapshot = new AppServerConfigSnapshot
            {
                ServiceTier = "flex",
                ToolOutputTokenLimit = 4096L
            };
            snapshot.Origins["service_tier"] = new AppServerConfigOrigin { SourceType = "user" };
            snapshot.Origins["tool_output_token_limit"] = new AppServerConfigOrigin { SourceType = "user" };

            using (var form = new OfficialCodexSettingsForm(snapshot))
            {
                InvokeApplySelection(form);

                Assert.That(GetField<CheckBox>(form, "_serviceTierCheckBox").ThreeState, Is.False);
                Assert.That(GetField<CheckBox>(form, "_serviceTierCheckBox").Checked, Is.False);
                Assert.That(form.ServiceTier, Is.EqualTo("flex"));
                Assert.That(form.ToolOutputTokenLimit, Is.EqualTo(4096L));
            }
        }

        [Test]
        public void FormatPermissionProfiles_IncludesIdsAndDescriptions()
        {
            var profiles = new[]
            {
                new AppServerPermissionProfileSummary { Id = "read-only", Description = "Read-only profile" },
                new AppServerPermissionProfileSummary { Id = "workspace-write" }
            };

            var text = OfficialCodexSettingsForm.FormatPermissionProfilesForTests(profiles);

            Assert.That(text, Does.Contain("read-only - Read-only profile"));
            Assert.That(text, Does.Contain("workspace-write"));
        }

        private static string GetChoiceValue(object item)
        {
            var property = item.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            return property.GetValue(item, null) as string;
        }

        private static T GetField<T>(OfficialCodexSettingsForm form, string fieldName) where T : class
        {
            var field = typeof(OfficialCodexSettingsForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(form);
        }

        private static void InvokeApplySelection(OfficialCodexSettingsForm form)
        {
            var method = typeof(OfficialCodexSettingsForm).GetMethod("ApplySelection", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(form, new object[0]);
        }
    }
}
