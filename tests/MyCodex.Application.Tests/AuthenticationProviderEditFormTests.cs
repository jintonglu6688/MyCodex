using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthenticationProviderEditFormTests
    {
        [TestCase("chatgpt")]
        [TestCase("official_api")]
        [TestCase("responses")]
        public async Task RenamingProviderWithoutCatalogDoesNotCreateModelOverride(string kind)
        {
            var provider = new AuthenticationProvider
            {
                Id = "saved", Name = "Original", Kind = kind, AccountId = "account",
                BaseUrl = "https://example.test/v1", Model = "gpt-6-astra"
            };
            AuthenticationProviderEdit saved = null;
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
                throw new InvalidOperationException("Unexpected request: " + method));
            using (var form = new AuthenticationProviderEditForm(client, provider, save: (edit, token) =>
            {
                saved = edit;
                return Task.FromResult(new AuthenticationSaveResult { Provider = provider, Reconnected = true });
            }))
            {
                FormTestDriver.Field<TextBox>(form, "_nameTextBox").Text = "Renamed";
                await form.SaveAsync();
                Assert.That(saved, Is.Not.Null);
                Assert.That(saved.Name, Is.EqualTo("Renamed"));
                Assert.That(saved.Model, Is.EqualTo("gpt-6-astra"));
                Assert.That(saved.Models, Is.Empty, "A default model ID must not become a custom model catalog.");
            }
        }

        [Test]
        public async Task ChatGptAuthorizationSurvivesSaveFailureAndRetryAppearsInProviderList()
        {
            var startReply = new TaskCompletionSource<IDictionary<string, object>>();
            var pollReply = new TaskCompletionSource<IDictionary<string, object>>();
            var actions = new List<string>();
            IDictionary<string, object> archivedProvider = null;
            string loginId = null;
            var saves = 0;
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
            {
                switch (method)
                {
                    case "gui/account/list":
                        return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "accounts", new object[0] } });
                    case "gui/account/login/start":
                        loginId = values["loginId"] as string;
                        return startReply.Task;
                    case "gui/account/login/get": return pollReply.Task;
                    case "gui/provider/save":
                        Assert.That(values["accountId"], Is.EqualTo("archived-account"));
                        Assert.That(values["kind"], Is.EqualTo("chatgpt"));
                        Assert.That(values.ContainsKey("apiKey"), Is.False);
                        if (++saves == 1) { throw new InvalidOperationException("offline"); }
                        archivedProvider = new Dictionary<string, object>(values) { ["version"] = "v1", ["globalApplied"] = false };
                        return Task.FromResult(archivedProvider);
                    case "status":
                        return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "protocolVersion", 3 }, { "route", new Dictionary<string, object>() } });
                    case "gui/provider/list":
                        return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "providers", archivedProvider == null ? new object[0] : new object[] { archivedProvider } } });
                    default: throw new InvalidOperationException("Unexpected method: " + method);
                }
            });
            using (var editor = new AuthenticationProviderEditForm(client, null, null,
                new AuthenticationProviderPreset { Id = "chatgpt", Name = "Personal ChatGPT", Kind = "chatgpt", Available = true }, false,
                code => actions.Add("copied"), url => actions.Add("opened"), save: async (edit, token) => new AuthenticationSaveResult { Provider = await client.SaveAsync(edit, token), Reconnected = true }))
            {
                var login = FormTestDriver.Field<AuthenticationLoginControl>(editor, "_login");
                var starting = login.StartLoginAsync();
                Assert.That(starting.IsCompleted, Is.False);
                Assert.That(FormTestDriver.Field<Button>(editor, "_saveButton").Enabled, Is.False);
                startReply.SetResult(new Dictionary<string, object>
                {
                    { "loginId", loginId }, { "status", "pending" }, { "userCode", "TEST-CODE" },
                    { "verificationUri", "https://auth.openai.com/codex/device" }
                });
                await starting;
                Assert.That(actions, Is.EqualTo(new[] { "copied", "opened" }));
                var polling = login.PollLoginAsync();
                Assert.That(polling.IsCompleted, Is.False);
                pollReply.SetResult(new Dictionary<string, object>
                {
                    { "loginId", loginId }, { "status", "completed" }, { "verificationUri", "https://auth.openai.com/codex/device" },
                    { "account", new Dictionary<string, object> { { "id", "archived-account" }, { "label", "Test account" } } }
                });
                await polling;
                Assert.That(login.AccountId, Is.EqualTo("archived-account"));
                Assert.That(FormTestDriver.Field<Button>(editor, "_saveButton").Enabled, Is.True);
                var status = FormTestDriver.Field<TextBox>(editor, "_statusLabel");
                Assert.That(status.Text, Does.Contain("Test account"), "The configuration status must show the login result, not only speak it.");
                Assert.That(status.ReadOnly, Is.True);
                Assert.That(status.TabStop, Is.False);
                await editor.SaveAsync();
                Assert.That(editor.SavedProvider, Is.Null);
                Assert.That(login.AccountId, Is.EqualTo("archived-account"));
                Assert.That(FormTestDriver.Field<Button>(editor, "_saveButton").Enabled, Is.True);
                await editor.SaveAsync();
                Assert.That(editor.SavedProvider.AccountId, Is.EqualTo("archived-account"));
                Assert.That(saves, Is.EqualTo(2));
                using (var center = new AuthenticationCenterForm(client))
                {
                    await center.RefreshAsync(editor.SavedProvider.Id);
                    var list = FormTestDriver.Field<ListView>(center, "_providersList");
                    Assert.That(list.Items.Count, Is.EqualTo(1));
                    Assert.That(list.Items[0].Text, Is.EqualTo("Personal ChatGPT"));
                    Assert.That(((AuthenticationProvider)list.Items[0].Tag).AccountId, Is.EqualTo("archived-account"));
                }
            }
        }

        [Test]
        public async Task SavedDraftRetainsStableIdAndModelMetadataAcrossRetry()
        {
            var writes = new List<IDictionary<string, object>>();
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
            {
                if (method == "status") { return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "protocolVersion", 3 }, { "route", new Dictionary<string, object>() } }); }
                if (method == "gui/provider/list") { return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "providers", new object[0] } }); }
                writes.Add(values);
                if (writes.Count == 1) { throw new InvalidOperationException("offline"); }
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "id", values["id"] }, { "version", "v1" }, { "globalApplied", false } });
            });
            using (var form = new AuthenticationProviderEditForm(client, template: new AuthenticationProviderPreset { Id = "custom", Name = "Test", Kind = "responses", AllowsProtocolSelection = true, BaseUrl = "https://example.test/v1", Model = "a" }, save: async (edit, token) => new AuthenticationSaveResult { Provider = await client.SaveAsync(edit, token), Reconnected = true }))
            {
                FormTestDriver.Field<AuthenticationModelsControl>(form, "_models").Models = new List<AuthenticationCatalogModel> { new AuthenticationCatalogModel { Model = "a", DisplayName = "Manual", Metadata = new Dictionary<string, object> { { "future", "keep" } } } };
                await form.SaveAsync();
                await form.SaveAsync();
                Assert.That(writes.Count, Is.EqualTo(2));
                Assert.That(writes[0]["id"], Is.EqualTo(writes[1]["id"]));
                Assert.That(writes[1]["presetId"], Is.EqualTo("custom"));
                Assert.That(form.SavedProvider, Is.Not.Null);
            }
        }

        [Test]
        public async Task DiscoveryDiscardsResponseAfterAddressChanges()
        {
            var response = new TaskCompletionSource<IDictionary<string, object>>();
            var client = new AuthenticationCenterClient("Test", (method, values, token) => response.Task);
            using (var form = new AuthenticationProviderEditForm(client, template: new AuthenticationProviderPreset { Id = "custom", Name = "Test", Kind = "responses", AllowsProtocolSelection = true, BaseUrl = "https://example.test/v1" }, save: async (edit, token) => new AuthenticationSaveResult { Provider = await client.SaveAsync(edit, token), Reconnected = true }))
            {
                var pending = form.FetchModelsAsync();
                FormTestDriver.Field<TextBox>(form, "_urlTextBox").Text = "https://changed.test/v1";
                response.SetResult(new Dictionary<string, object> { { "models", new[] { new Dictionary<string, object> { { "model", "wrong-server" } } } } });
                await pending;
                Assert.That(FormTestDriver.Field<AuthenticationModelsControl>(form, "_models").Models, Is.Empty);
            }
        }

        [Test]
        public void AdvancedUnknownValuesSurviveProtocolVisibilityChanges()
        {
            var unknown = new Dictionary<string, object> { { "nested", "unchanged" } };
            using (var advanced = new AuthenticationAdvancedControl(new Dictionary<string, object>
            {
                { "future", unknown }, { "requestBody", new Dictionary<string, object> { { "nested", unknown } } },
                { "codexChatReasoning", new Dictionary<string, object> { { "effortValueMode", "numeric" } } }
            }))
            {
                advanced.Kind = "chat_completions";
                var first = advanced.Read();
                Assert.That(first["future"], Is.SameAs(unknown));
                Assert.That(((Dictionary<string, object>)first["requestBody"])["nested"], Is.SameAs(unknown));
                Assert.That(((Dictionary<string, object>)first["codexChatReasoning"])["effortValueMode"], Is.EqualTo("numeric"));
                advanced.Kind = "responses";
                Assert.That(advanced.Read()["future"], Is.SameAs(unknown));
            }
        }

        [Test]
        public void AnthropicOptionsAreExplicitAndRemovedWhenProtocolChanges()
        {
            using (var advanced = new AuthenticationAdvancedControl(new Dictionary<string, object>
            {
                { "codexChatReasoning", new Dictionary<string, object> { { "supportsThinking", true } } },
                { "future", "preserved" }
            }))
            {
                advanced.Kind = "anthropic";
                FormTestDriver.Field<CheckBox>(advanced, "_impersonate").Checked = true;
                var anthropic = advanced.Read();
                Assert.That(anthropic["anthropicAuthHeader"], Is.EqualTo("x-api-key"));
                Assert.That(anthropic["impersonateClaudeCode"], Is.True);
                Assert.That(anthropic.ContainsKey("codexChatReasoning"), Is.False);
                Assert.That(anthropic.ContainsKey("promptCacheRouting"), Is.False);
                advanced.Kind = "chat_completions";
                var chat = advanced.Read();
                Assert.That(chat.ContainsKey("impersonateClaudeCode"), Is.False);
                Assert.That(chat.ContainsKey("anthropicAuthHeader"), Is.False);
                Assert.That(chat["future"], Is.EqualTo("preserved"));
            }
        }

        [Test]
        public async Task ClearingStoredKeyDoesNotUseStoredProviderCredentialForDiscovery()
        {
            IDictionary<string, object> fetch = null;
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
            {
                fetch = values;
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "models", new object[0] } });
            });
            using (var form = new AuthenticationProviderEditForm(client, new AuthenticationProvider { Id = "a", Name = "A", Kind = "responses", BaseUrl = "https://example.test/v1", Model = "a", HasApiKey = true }, save: async (edit, token) => new AuthenticationSaveResult { Provider = await client.SaveAsync(edit, token), Reconnected = true }))
            {
                FormTestDriver.Field<CheckBox>(form, "_clearKeyCheckBox").Checked = true;
                await form.FetchModelsAsync();
                Assert.That(fetch.ContainsKey("providerId"), Is.False);
                Assert.That(fetch.ContainsKey("apiKey"), Is.False);
            }
        }

        [Test]
        public async Task DeepSeekPresetLoadsModelsAndSinglePageSectionsRemainKeyboardOperable()
        {
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
                Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "presets", new object[] { new Dictionary<string, object>
                    {
                        { "id", "cc-switch-deepseek" }, { "name", "DeepSeek" }, { "kind", "responses" },
                        { "baseUrl", "https://api.deepseek.com" }, { "model", "deepseek-v4-flash" },
                        { "available", true }, { "models", new object[]
                        {
                            new Dictionary<string, object> { { "model", "deepseek-v4-flash" }, { "displayName", "DeepSeek V4 Flash" } },
                            new Dictionary<string, object> { { "model", "deepseek-v4-pro" }, { "displayName", "DeepSeek V4 Pro" } }
                        } }
                    } } }
                }));
            var presets = await client.ReadPresetsAsync(CancellationToken.None);
            using (var form = new AuthenticationProviderEditForm(client, template: presets[0], save: async (edit, token) => new AuthenticationSaveResult { Provider = await client.SaveAsync(edit, token), Reconnected = true }))
            {
                form.ShowInTaskbar = false;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var content = FormTestDriver.Field<Panel>(form, "_content");
                Assert.That(content.AutoScroll, Is.True);
                Assert.That(FormTestDriver.Field<TableLayoutPanel>(form, "_sections").Controls.Count, Is.EqualTo(4));
                var models = FormTestDriver.Field<AuthenticationModelsControl>(form, "_models");
                Assert.That(FormTestDriver.Field<TextBox>(form, "_urlTextBox").Text, Is.EqualTo("https://api.deepseek.com"));
                Assert.That(models.DefaultModel, Is.EqualTo("deepseek-v4-flash"));
                System.Windows.Forms.Application.DoEvents();
                var list = FormTestDriver.Field<ListView>(models, "_list");
                Assert.That(list.Items.Count, Is.EqualTo(2));
                var clear = FormTestDriver.Field<CheckBox>(form, "_clearKeyCheckBox");
                clear.Focus();
                form.SelectNextControl(clear, true, true, true, false);
                Assert.That(list.Visible && list.Enabled && list.Focused, Is.True,
                    "Tab after basic settings must enter the model list.");
                System.Windows.Forms.Application.DoEvents();
                var advanced = FormTestDriver.Field<AuthenticationAdvancedControl>(form, "_advanced");
                var fullUrl = FormTestDriver.Field<CheckBox>(advanced, "_full");
                var add = FormTestDriver.Field<Button>(models, "_fetch").Parent.Controls[1];
                add.Focus();
                form.SelectNextControl(add, true, true, true, false);
                Assert.That(fullUrl.Visible && fullUrl.Enabled && fullUrl.Focused, Is.True,
                    "Tab after model actions must enter the advanced settings.");
            }
        }

        [Test]
        public void ChatGptLoginControlsAreReachableWithoutModelOrAdvancedSections()
        {
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
                Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "accounts", new object[0] }
                }));
            using (var form = new AuthenticationProviderEditForm(client, new AuthenticationProvider
            {
                Id = "chatgpt-test", Name = "ChatGPT", Kind = "chatgpt", AccountId = "saved-account"
            }, save: async (edit, token) => new AuthenticationSaveResult { Provider = await client.SaveAsync(edit, token), Reconnected = true }))
            {
                form.ShowInTaskbar = false;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Assert.That(FormTestDriver.Field<AuthenticationModelsControl>(form, "_models"), Is.Null);
                Assert.That(FormTestDriver.Field<AuthenticationAdvancedControl>(form, "_advanced"), Is.Null);
                Assert.That(FormTestDriver.Field<TableLayoutPanel>(form, "_sections").Controls.Count, Is.EqualTo(2));
                var name = FormTestDriver.Field<TextBox>(form, "_nameTextBox");
                var login = FormTestDriver.Field<AuthenticationLoginControl>(form, "_login");
                name.Focus();
                form.SelectNextControl(name, true, true, true, false);
                Assert.That(FormTestDriver.Field<TextBox>(login, "_code").Focused, Is.True,
                    "Tab from the provider name must enter the sign-in controls.");
                form.SelectNextControl(FormTestDriver.Field<TextBox>(login, "_code"), true, true, true, false);
                Assert.That(FormTestDriver.Field<Button>(login, "_start").Focused, Is.True,
                    "Tab must skip the sign-in status and reach the login button.");
                form.SelectNextControl(FormTestDriver.Field<Button>(login, "_start"), false, true, true, false);
                Assert.That(FormTestDriver.Field<TextBox>(login, "_code").Focused, Is.True,
                    "Shift+Tab must also skip the sign-in status.");
            }
        }

        [Test]
        public async Task FetchedModelsMoveFocusToTheModelList()
        {
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
                Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "models", new object[]
                {
                    new Dictionary<string, object> { { "model", "first" } },
                    new Dictionary<string, object> { { "model", "second" } }
                } } }));
            using (var form = new AuthenticationProviderEditForm(client, template:
                new AuthenticationProviderPreset { Kind = "responses", Name = "Test", BaseUrl = "https://example.test/v1" }))
            {
                form.ShowInTaskbar = false;
                form.Show();
                ErrorBoxRecorder.Clear();
                await form.FetchModelsAsync();
                var list = FormTestDriver.Field<ListView>(FormTestDriver.Field<AuthenticationModelsControl>(form, "_models"), "_list");
                Assert.That(list.Items.Count, Is.EqualTo(2));
                Assert.That(list.Focused, Is.True);
                Assert.That(list.SelectedItems.Count, Is.EqualTo(1));
                Assert.That(list.FocusedItem, Is.SameAs(list.SelectedItems[0]));
                Assert.That(ErrorBoxRecorder.Messages, Is.Empty);
            }
        }

        [Test]
        public async Task FailedModelFetchAndMissingRequiredFieldsOpenErrorBoxes()
        {
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
                throw new AuthenticationCenterException("upstream_unreachable"));
            using (var form = new AuthenticationProviderEditForm(client, template:
                new AuthenticationProviderPreset { Kind = "responses", Name = "Test", BaseUrl = "https://example.test/v1" },
                save: (edit, token) => throw new InvalidOperationException("Save must not run.")))
            {
                form.ShowInTaskbar = false;
                form.Show();
                ErrorBoxRecorder.Clear();
                await form.FetchModelsAsync();
                var status = FormTestDriver.Field<Control>(form, "_statusLabel");
                Assert.That(ErrorBoxRecorder.Messages, Is.EqualTo(new[] { status.Text }));
                FormTestDriver.Field<TextBox>(form, "_nameTextBox").Text = string.Empty;
                await form.SaveAsync();
                Assert.That(ErrorBoxRecorder.Messages, Has.Count.EqualTo(2));
                Assert.That(ErrorBoxRecorder.Messages[1], Is.EqualTo(status.Text));
                Assert.That(FormTestDriver.Field<TextBox>(form, "_nameTextBox").Focused, Is.True, "Focus returns to the field to fix.");
            }
        }

        [Test]
        public async Task ChatGptSaveClearsLegacyCatalogAndNeverFetchesModels()
        {
            AuthenticationProviderEdit saved = null;
            var provider = new AuthenticationProvider { Id = "account", Name = "ChatGPT", Kind = "chatgpt", AccountId = "saved",
                Model = "selected-model", Models = new List<AuthenticationCatalogModel> { new AuthenticationCatalogModel { Model = "wrong-model" } } };
            var client = new AuthenticationCenterClient("Test", (method, values, token) => throw new InvalidOperationException("Unexpected request: " + method));
            using (var form = new AuthenticationProviderEditForm(client, provider, save: (edit, token) =>
            {
                saved = edit;
                return Task.FromResult(new AuthenticationSaveResult { Provider = provider });
            }))
            {
                await form.FetchModelsAsync();
                await form.SaveAsync();
                Assert.That(saved.Models, Is.Empty);
                Assert.That(saved.Model, Is.EqualTo("selected-model"));
            }
        }

        [TestCase("responses")]
        [TestCase("chat_completions")]
        [TestCase("anthropic")]
        public void SinglePageScrollsToLastSettingAtMinimumSize(string kind)
        {
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
                throw new InvalidOperationException("Unexpected request: " + method));
            using (var form = new AuthenticationProviderEditForm(client, template:
                new AuthenticationProviderPreset { Kind = kind, Name = "Test", Model = "model", BaseUrl = "https://example.test/v1" }))
            {
                form.ShowInTaskbar = false;
                form.Size = form.MinimumSize;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var content = FormTestDriver.Field<Panel>(form, "_content");
                var advanced = FormTestDriver.Field<AuthenticationAdvancedControl>(form, "_advanced");
                var query = FormTestDriver.Field<AuthenticationKeyValueListControl>(advanced, "_query");
                var add = FormTestDriver.Field<Button>(query, "_add");
                add.Focus();
                System.Windows.Forms.Application.DoEvents();
                Assert.That(add.Focused, Is.True);
                Assert.That(content.ClientRectangle.Contains(content.PointToClient(add.PointToScreen(new System.Drawing.Point(1, add.Height - 1)))), Is.True,
                    "Keyboard focus must scroll the last setting into view.");
                Assert.That(content.HorizontalScroll.Visible, Is.False);
                Assert.That(FormTestDriver.Field<Button>(form, "_saveButton").Visible, Is.True);
                form.SelectNextControl(add, true, true, true, false);
                Assert.That(FormTestDriver.Field<Button>(form, "_saveButton").Focused, Is.True,
                    "Tab from the last setting must skip the configuration status and reach Save.");
            }
        }
    }
}
