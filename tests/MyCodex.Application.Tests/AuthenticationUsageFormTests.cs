using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class AuthenticationCenterFormTests
    {
        [Test]
        public void UsageMenuIsOfferedOnlyForSavedProvidersOtherThanChatGpt()
        {
            var client = new FakeClient { SupportsProviderUsage = true };
            var chatGpt = Provider("b");
            chatGpt.Kind = "chatgpt";
            client.Providers.AddRange(new[] { Provider("a"), chatGpt });
            using (var form = new AuthenticationCenterForm(client))
            {
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                Assert.That(form.Handle, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(list.Handle, Is.Not.EqualTo(IntPtr.Zero));
                form.RefreshAsync().GetAwaiter().GetResult();
                var usage = FormTestDriver.Field<MenuItem>(form, "_usageMenuItem");
                list.Items[0].Selected = true;
                Assert.That(usage.Visible && usage.Enabled, Is.True);
                list.Items[0].Selected = false;
                list.Items[1].Selected = true;
                Assert.That(usage.Visible, Is.False, "ChatGPT quota comes from the App Server.");
            }
            client.SupportsProviderUsage = false;
            using (var form = new AuthenticationCenterForm(client))
            {
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                Assert.That(list.Handle, Is.Not.EqualTo(IntPtr.Zero));
                form.RefreshAsync().GetAwaiter().GetResult();
                list.Items[0].Selected = true;
                Assert.That(FormTestDriver.Field<MenuItem>(form, "_usageMenuItem").Visible, Is.False);
            }
        }

        [Test]
        public void UsageSettingsShowOnlyTheFieldsOfTheSelectedMethodWithNames()
        {
            var client = new FakeClient();
            client.Usage.Detected = "balance";
            using (var form = new AuthenticationUsageForm(client, client.Usage))
            {
                // Final values are in place before the window opens, so nothing changes under the screen reader.
                var template = FormTestDriver.Field<ComboBox>(form, "_template");
                Assert.That(template.SelectedItem, Is.EqualTo("balance"), "The detected method is preselected.");
                Assert.That(FormTestDriver.Field<CheckBox>(form, "_enabled").Checked, Is.True);
                Assert.That(form.ActiveControl, Is.SameAs(FormTestDriver.Field<CheckBox>(form, "_enabled")));
                var changes = 0;
                FormTestDriver.Field<CheckBox>(form, "_enabled").CheckedChanged += (sender, args) => changes++;
                template.SelectedIndexChanged += (sender, args) => changes++;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Assert.That(changes, Is.Zero, "Opening the window changes no control state.");
                Assert.That(FormTestDriver.Field<ThemedTextBox>(form, "_status").Text, Does.Contain(
                    AuthenticationUsagePresentation.TemplateName("balance")));
                Assert.That(Visible(form), Is.EquivalentTo(new[] { "_enabled", "_template", "_interval" }));

                template.SelectedItem = "newapi";
                Assert.That(Visible(form), Is.EquivalentTo(new[] { "_enabled", "_template", "_baseUrl", "_accessToken", "_userId", "_timeout", "_interval" }));
                template.SelectedItem = "custom";
                Assert.That(Visible(form), Is.EquivalentTo(new[] { "_enabled", "_template", "_baseUrl", "_apiKey", "_accessToken", "_userId", "_timeout", "_interval", "_code" }),
                    "Clear options appear only when a secret is saved.");
                foreach (var name in Visible(form))
                {
                    Assert.That(FormTestDriver.Field<Control>(form, name).AccessibleName, Is.Not.Empty.And.Not.Contains("&"), name);
                }
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(560));
            }
        }

        [Test]
        public void SavingUsageSendsVisibleFieldsClearsSecretsAndShowsTheTestResult()
        {
            var client = new FakeClient
            {
                Usage = new AuthenticationUsageSettings
                {
                    ProviderId = "a", ProviderName = "Provider A", Configured = true, Enabled = true, TemplateType = "general",
                    BaseUrl = "https://usage.example.test", HasApiKey = true, UserId = "old", Timeout = 10, AutoQueryInterval = 5
                },
                UsageResult = new AuthenticationUsageResult
                {
                    Success = true,
                    Items = new List<AuthenticationUsageItem> { new AuthenticationUsageItem { Remaining = 12.5m, Unit = "USD" } }
                }
            };
            using (var form = new AuthenticationUsageForm(client, client.Usage))
            {
                form.Show();
                Assert.That(form.Text, Does.Contain("Provider A"));
                Assert.That(FormTestDriver.Field<CheckBox>(form, "_clearApiKey").Visible, Is.True);
                var status = FormTestDriver.Field<ThemedTextBox>(form, "_status");

                FormTestDriver.Field<ThemedTextBox>(form, "_timeout").Text = "1";
                form.SaveAsync().GetAwaiter().GetResult();
                Assert.That(client.UsageSaveCalls, Is.Zero);
                Assert.That(status.Text, Is.EqualTo(UiLocalization.Text("AuthenticationUsage.Invalid.Timeout", "Enter a timeout from 2 to 30 seconds.")));

                FormTestDriver.Field<ThemedTextBox>(form, "_timeout").Text = "15";
                FormTestDriver.Field<ThemedTextBox>(form, "_interval").Text = "0";
                FormTestDriver.Field<ThemedTextBox>(form, "_apiKey").Text = " synthetic-usage-key ";
                form.SaveAsync().GetAwaiter().GetResult();
                var edit = client.LastUsageEdit;
                Assert.That(edit.TemplateType, Is.EqualTo("general"));
                Assert.That(edit.ApiKey, Is.EqualTo("synthetic-usage-key"));
                Assert.That(edit.BaseUrl, Is.EqualTo("https://usage.example.test"));
                Assert.That(edit.AccessToken, Is.Null, "Hidden fields are not sent.");
                Assert.That(edit.UserId, Is.Null);
                Assert.That(edit.Timeout, Is.EqualTo(15));
                Assert.That(edit.AutoQueryInterval, Is.Zero);
                Assert.That(form.Saved, Is.True);
                Assert.That(client.UsageQueryCalls, Is.EqualTo(1), "Saving tests the query.");
                Assert.That(FormTestDriver.Field<ThemedTextBox>(form, "_apiKey").Text, Is.Empty, "The entered key is not kept on screen.");
                Assert.That(status.Text, Does.Contain(UiLocalization.Text("AuthenticationUsage.TestPassed", "Saved. The test query succeeded:")));
                Assert.That(status.Text, Does.Contain(string.Format(UiLocalization.Text("ProviderUsage.Details.Remaining", "Remaining: {0}"), 12.5m.ToString("0.##") + " USD")));
                Assert.That(status.Text, Does.Not.Contain("synthetic-usage-key"));
            }
        }

        [Test]
        public void ProviderUsageShowsTheCurrentProviderAndRefreshesOnItsInterval()
        {
            var client = new FakeClient { SupportsProviderUsage = true, Current = "a" };
            client.Providers.Add(Provider("a"));
            client.Usage = new AuthenticationUsageSettings
            {
                ProviderId = "a", ProviderName = "a", Configured = true, Enabled = true, TemplateType = "newapi", AutoQueryInterval = 7
            };
            client.UsageResult = new AuthenticationUsageResult
            {
                Success = true,
                Items = new List<AuthenticationUsageItem>
                {
                    new AuthenticationUsageItem { PlanName = "Pro", Remaining = 3, Total = 10, Unit = "USD", Extra = "{\"resetsAt\":\"2026-11-10T00:00:00Z\",\"usedValueUsd\":1}" }
                }
            };
            using (var form = new ProviderUsageForm(client))
            {
                form.RefreshAsync(false).GetAwaiter().GetResult();
                var lines = FormTestDriver.Field<ListBox>(form, "_detailsListBox").Items.Cast<string>().ToList();
                Assert.That(lines[0], Is.EqualTo(string.Format(UiLocalization.Text("ProviderUsage.Details.Provider", "Provider: {0}"), "a")));
                Assert.That(lines, Does.Contain(string.Format(UiLocalization.Text("ProviderUsage.Details.Plan", "Plan: {0}"), "Pro")));
                Assert.That(lines, Does.Contain(string.Format(UiLocalization.Text("ProviderUsage.Details.Total", "Total: {0}"), "10 USD")));
                Assert.That(lines.Any(line => line.StartsWith(string.Format(UiLocalization.Text("ProviderUsage.Details.Reset", "Resets: {0}"), string.Empty), StringComparison.Ordinal)), Is.True);
                var timer = FormTestDriver.Field<Timer>(form, "_timer");
                Assert.That(timer.Enabled, Is.True);
                Assert.That(timer.Interval, Is.EqualTo(7 * 60000));
                Assert.That(FormTestDriver.Field<Button>(form, "_settingsButton").Enabled, Is.True);

                client.Usage = new AuthenticationUsageSettings { ProviderId = "a", ProviderName = "a" };
                form.RefreshAsync(false).GetAwaiter().GetResult();
                lines = FormTestDriver.Field<ListBox>(form, "_detailsListBox").Items.Cast<string>().ToList();
                Assert.That(lines.Last(), Is.EqualTo(UiLocalization.Text("ProviderUsage.Status.NotConfigured",
                    "Usage queries are not turned on for this provider. Choose Usage settings to set them up.")));
                Assert.That(timer.Enabled, Is.False);
                Assert.That(client.UsageQueryCalls, Is.EqualTo(1), "A provider without usage settings is not queried.");
            }
            client.SupportsProviderUsage = false;
            using (var form = new ProviderUsageForm(client))
            {
                form.RefreshAsync(false).GetAwaiter().GetResult();
                Assert.That(FormTestDriver.Field<ListBox>(form, "_detailsListBox").Items.Cast<string>().Single(), Is.EqualTo(
                    UiLocalization.Text("ProviderUsage.Status.BackendUnsupported",
                        "The authentication backend of this target cannot query usage. Update it from the authentication center.")));
                Assert.That(FormTestDriver.Field<Button>(form, "_settingsButton").Enabled, Is.False);
            }
        }

        [Test]
        public void UsageLinesDescribeFailuresInvalidPlansAndPercentages()
        {
            Assert.That(AuthenticationUsagePresentation.Lines(new AuthenticationUsageResult { Error = "401" }), Is.EqualTo(new[]
            {
                string.Format(UiLocalization.Text("ProviderUsage.Details.Failed", "Query failed: {0}"), "401")
            }));
            var lines = AuthenticationUsagePresentation.Lines(new AuthenticationUsageResult
            {
                Success = true,
                Items = new List<AuthenticationUsageItem>
                {
                    new AuthenticationUsageItem { PlanName = "Weekly", Used = 25, Remaining = 75, Unit = "%", Extra = "next Monday" },
                    new AuthenticationUsageItem { IsValid = false, InvalidMessage = "expired", Remaining = 1 }
                }
            });
            Assert.That(lines, Is.EqualTo(new[]
            {
                string.Format(UiLocalization.Text("ProviderUsage.Details.Plan", "Plan: {0}"), "Weekly"),
                string.Format(UiLocalization.Text("ProviderUsage.Details.Remaining", "Remaining: {0}"), "75%"),
                string.Format(UiLocalization.Text("ProviderUsage.Details.Used", "Used: {0}"), "25%"),
                string.Format(UiLocalization.Text("ProviderUsage.Details.Extra", "Details: {0}"), "next Monday"),
                string.Format(UiLocalization.Text("ProviderUsage.Details.Invalid", "Not valid: {0}"), "expired")
            }));
        }

        private static List<string> Visible(Form form)
        {
            return new[] { "_enabled", "_template", "_baseUrl", "_apiKey", "_clearApiKey", "_accessToken", "_clearAccessToken", "_userId", "_timeout", "_interval", "_code" }
                .Where(name => FormTestDriver.Field<Control>(form, name).Visible).ToList();
        }
    }
}
