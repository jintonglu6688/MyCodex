using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthenticationConfigEditingTests
    {
        [Test]
        public async Task EditorFocusNavigationValidationAndUncertainSaveRetainDraft()
        {
            var code = "invalid_config";
            using (var form = new AuthenticationConfigEditForm("Configuration", "Hint", "bad=[", (text, token) =>
                throw new AuthenticationCenterException(code)))
            {
                form.ShowInTaskbar = false;
                form.Size = form.MinimumSize;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var editor = Field<TextBox>(form, "_editor");
                var save = Field<Button>(form, "_saveButton");
                var cancel = Field<Button>(form, "_cancelButton");
                Assert.That(editor.Focused, Is.True);
                Assert.That(editor.AccessibleName, Is.Not.Empty);
                Assert.That(editor.Multiline && editor.AcceptsReturn && !editor.AcceptsTab, Is.True);
                Assert.That(form.AcceptButton, Is.Null, "Enter belongs to the multiline editor.");
                form.SelectNextControl(editor, true, true, true, false);
                Assert.That(save.Focused, Is.True);
                form.SelectNextControl(save, true, true, true, false);
                Assert.That(cancel.Focused, Is.True);
                Assert.That(form.ClientRectangle.Contains(form.PointToClient(save.PointToScreen(System.Drawing.Point.Empty))), Is.True);
                await form.SaveAsync();
                Assert.That(editor.Text, Is.EqualTo("bad=["));
                Assert.That(Field<Label>(form, "_status").Text, Is.Not.Empty);
                Assert.That(Field<Label>(form, "_status").TabStop, Is.False);
                Assert.That(save.Enabled, Is.True);
                code = "operation_unknown";
                await form.SaveAsync();
                Assert.That(form.SaveOutcomeUnknown, Is.True);
                Assert.That(save.Enabled, Is.False);
                Assert.That(cancel.Enabled, Is.True);
            }
        }

        [Test]
        public async Task ProviderTomlUpdatesDraftButOnlyOuterSavePersists()
        {
            AuthenticationProviderEdit saved = null;
            var provider = new AuthenticationProvider { Id = "one", Name = "One", Kind = "responses", Model = "old", Version = "v1" };
            var client = new AuthenticationCenterClient("Test", (method, values, token) => throw new InvalidOperationException(method));
            using (var form = new AuthenticationProviderEditForm(client, provider, supportsContextSettings: true, supportsConfigEditing: true,
                save: (edit, token) => { saved = edit; return Task.FromResult(new AuthenticationSaveResult { Provider = provider }); }))
            {
                form.ApplyConfigDraft(new AuthenticationConfigDocument
                {
                    Text = "# custom\nmodel='new'", Model = "new", BaseUrl = "https://example.test/v1",
                    Advanced = new Dictionary<string, object> { { "modelContextWindow", 1000000L }, { "autoCompactTokenLimit", 880000L } }
                });
                Assert.That(saved, Is.Null);
                Assert.That(provider.Model, Is.EqualTo("old"));
                Assert.That(Field<AuthenticationModelsControl>(form, "_models").Models, Is.Empty, "Editing TOML must not invent model mappings.");
                Assert.That(Field<TextBox>(form, "_urlTextBox").Text, Is.EqualTo("https://example.test/v1"));
                await form.SaveAsync();
                Assert.That(saved.ConfigToml, Is.EqualTo("# custom\nmodel='new'"));
                Assert.That(saved.Model, Is.EqualTo("new"));
                Assert.That(saved.ExpectedVersion, Is.EqualTo("v1"));
                Assert.That(saved.Advanced["autoCompactTokenLimit"], Is.EqualTo(880000L));
            }
        }

        [Test]
        public async Task FullConfigClientSendsEditorDraftAndReportsConflictsWithoutWriting()
        {
            var calls = new List<KeyValuePair<string, IDictionary<string, object>>>();
            var conflict = true;
            var client = new AuthenticationCenterClient("Target", (method, values, token) =>
            {
                calls.Add(new KeyValuePair<string, IDictionary<string, object>>(method, values));
                if (method == "gui/config/provider/view")
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "text", "approval_policy = \"never\"\nmodel = \"m\"\n" },
                        { "inactive", new object[] { new Dictionary<string, object> { { "path", "sandbox_mode" }, { "value", "\"read-only\"" } } } }
                    });
                if (method == "gui/config/provider/preview")
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "text", "model = \"m\"\n" }, { "baseUrl", "https://example.test/v1" }, { "model", "m" },
                        { "advanced", new Dictionary<string, object>() }, { "globalChanges", new[] { "approval_policy" } }
                    });
                if (conflict)
                {
                    conflict = false;
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                        { { "editorConflict", new Dictionary<string, object> { { "keys", new[] { "approval_policy" } } } } });
                }
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    { { "id", "one" }, { "version", "v2" }, { "globalApplied", true } });
            });
            var edit = new AuthenticationProviderEdit { Id = "one", Name = "One", Kind = "responses", Model = "m" };
            var view = await client.ViewProviderConfigAsync(edit, CancellationToken.None);
            Assert.That(view.Inactive.Single().Path, Is.EqualTo("sandbox_mode"));
            Assert.That(calls.Last().Value.ContainsKey("editor"), Is.False);
            var preview = await client.PreviewProviderConfigAsync(edit, "edited", CancellationToken.None);
            Assert.That(calls.Last().Value.ContainsKey("editorBase"), Is.False, "Row-only previews send no base.");
            edit.EditorBase = view.Text;
            preview = await client.PreviewProviderConfigAsync(edit, "edited", CancellationToken.None);
            Assert.That(calls.Last().Value["editorBase"], Is.EqualTo(view.Text));
            Assert.That(preview.GlobalChanges, Is.EqualTo(new[] { "approval_policy" }));
            edit.EditorText = "edited";
            var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => client.SaveAsync(edit, CancellationToken.None));
            Assert.That(error.Code, Is.EqualTo("editor_conflict"));
            Assert.That(error.Keys, Is.EqualTo(new[] { "approval_policy" }));
            var editor = (IDictionary<string, object>)calls.Last().Value["editor"];
            Assert.That(editor["base"], Is.EqualTo(view.Text));
            Assert.That(editor["text"], Is.EqualTo("edited"));
            Assert.That(editor.ContainsKey("onConflict"), Is.False);
            edit.EditorConflictPolicy = "keepTheirs";
            Assert.That((await client.SaveAsync(edit, CancellationToken.None)).GlobalApplied, Is.True);
            Assert.That(((IDictionary<string, object>)calls.Last().Value["editor"])["onConflict"], Is.EqualTo("keepTheirs"));
        }

        [TestCase("keepMine")]
        [TestCase("keepTheirs")]
        [TestCase(null)]
        public async Task ProviderSaveAsksWhichSideWinsOnlyAfterAConflictAndRetriesOnce(string choice)
        {
            var saves = new List<AuthenticationProviderEdit>();
            var provider = new AuthenticationProvider { Id = "one", Name = "One", Kind = "responses", Model = "m", Version = "v1" };
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
                Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "text", "model = \"m\"\n" }, { "baseUrl", "https://example.test/v1" }, { "model", "m" },
                    { "advanced", new Dictionary<string, object>() }, { "globalChanges", new[] { "approval_policy" } }
                }));
            using (var form = new AuthenticationProviderEditForm(client, provider, supportsConfigEditing: true, supportsFullConfigEditor: true,
                save: (edit, token) =>
                {
                    saves.Add(new AuthenticationProviderEdit { EditorBase = edit.EditorBase, EditorText = edit.EditorText, EditorConflictPolicy = edit.EditorConflictPolicy });
                    if (saves.Count == 1) throw new AuthenticationCenterException("editor_conflict", new[] { "approval_policy" });
                    return Task.FromResult(new AuthenticationSaveResult { Provider = provider });
                }))
            {
                IReadOnlyList<string> asked = null;
                form.ChooseConflictPolicy = keys => { asked = keys; return choice; };
                await form.ApplyFullConfigDraftAsync(new AuthenticationProviderEdit { Id = "one", Name = "One", Kind = "responses", Model = "m" },
                    "approval_policy = \"never\"\n", "approval_policy = \"untrusted\"\n", null, CancellationToken.None);
                Assert.That(Field<TextBox>(form, "_statusLabel").Text, Does.Contain("approval_policy"));
                await form.SaveAsync();
                Assert.That(asked, Is.EqualTo(new[] { "approval_policy" }));
                Assert.That(saves[0].EditorBase, Is.EqualTo("approval_policy = \"never\"\n"));
                Assert.That(saves[0].EditorText, Is.EqualTo("approval_policy = \"untrusted\"\n"));
                Assert.That(saves[0].EditorConflictPolicy, Is.Null, "The first save refuses conflicts.");
                if (choice == null)
                {
                    Assert.That(saves.Count, Is.EqualTo(1));
                    Assert.That(form.DialogResult, Is.Not.EqualTo(DialogResult.OK));
                    Assert.That(Field<TextBox>(form, "_statusLabel").Text, Is.Not.Empty);
                }
                else
                {
                    Assert.That(saves.Count, Is.EqualTo(2));
                    Assert.That(saves[1].EditorConflictPolicy, Is.EqualTo(choice));
                    Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK));
                }
            }
        }

        [Test]
        public void ConflictDialogIsKeyboardOperableAndReturnsTheChosenSide()
        {
            using (var form = new AuthenticationEditorConflictForm(new[] { "approval_policy", "sandbox_mode" }))
            {
                form.ShowInTaskbar = false;
                form.Size = form.MinimumSize;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var keys = Field<ListBox>(form, "_keys");
                Assert.That(keys.Focused, Is.True);
                Assert.That(keys.Items.Count, Is.EqualTo(2));
                Assert.That(keys.AccessibleName, Is.Not.Empty);
                var buttons = keys.Parent.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().ToList();
                Assert.That(buttons.Count, Is.EqualTo(3));
                Assert.That(buttons.All(button => !string.IsNullOrEmpty(button.AccessibleName) && !button.AccessibleName.Contains("&")), Is.True);
                form.SelectNextControl(keys, true, true, true, false);
                Assert.That(buttons[0].Focused, Is.True);
                Assert.That(form.CancelButton, Is.SameAs(buttons[2]));
                Assert.That(form.ClientRectangle.Contains(form.PointToClient(buttons[2].PointToScreen(System.Drawing.Point.Empty))), Is.True);
                buttons[1].PerformClick();
                Assert.That(form.Policy, Is.EqualTo("keepTheirs"));
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK));
            }
        }

        [Test]
        public void InactiveSettingsAreShownBelowTheEditorWithoutJoiningTheTabOrder()
        {
            using (var form = new AuthenticationConfigEditForm("Configuration", "Hint", "model = 'm'", "sandbox_mode = \"read-only\"", (text, token) => Task.CompletedTask))
            {
                var details = Field<TextBox>(form, "_details");
                Assert.That(details.ReadOnly && !details.TabStop, Is.True);
                Assert.That(details.AccessibleName, Is.Not.Empty);
                Assert.That(details.Text, Does.Contain("sandbox_mode"));
            }
            using (var form = new AuthenticationConfigEditForm("Configuration", "Hint", "model = 'm'", (text, token) => Task.CompletedTask))
            {
                Assert.That(Field<TextBox>(form, "_details"), Is.Null);
            }
        }

        [Test]
        public async Task EditorRpcUsesTargetClientAndPreservesEmptyDraftAndVersion()
        {
            var calls = new List<string>();
            var client = new AuthenticationCenterClient("Target", (method, values, token) =>
            {
                calls.Add(method);
                Assert.That(method, Is.EqualTo("gui/config/provider/preview"));
                Assert.That(values["editedText"], Is.EqualTo(""));
                Assert.That(values["configToml"], Is.EqualTo("# draft"));
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                { { "text", "" }, { "baseUrl", "" }, { "model", "" }, { "advanced", new Dictionary<string, object>() } });
            });
            var draft = await client.PreviewProviderConfigAsync(new AuthenticationProviderEdit { Id = "draft", Kind = "responses", ConfigToml = "# draft" }, "", CancellationToken.None);
            Assert.That(draft.Text, Is.Empty);
            Assert.That(calls.Count, Is.EqualTo(1));
        }

        private static T Field<T>(object owner, string name) where T : class
        {
            return owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner) as T;
        }
    }
}
