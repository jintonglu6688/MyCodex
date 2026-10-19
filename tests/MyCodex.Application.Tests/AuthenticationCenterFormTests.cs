using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Controls;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthenticationCenterFormTests
    {
        [Test]
        public void UnreachableBackendHoldingItsStoreExplainsRecoveryInsteadOfGenericFailure()
        {
            var text = AuthenticationCenterUiText.Error(new AuthenticationCenterException("store_busy"));
            Assert.That(text, Does.Contain("mycodex-auth-host"));
            Assert.That(text, Is.Not.EqualTo(AuthenticationCenterUiText.Error(new AuthenticationCenterException("unmapped_code"))));
        }

        [TestCase(true, true, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        public void SessionHistoryIsOfferedOnlyWhenTheBackendSupportsIt(bool supported, bool settable, bool offered)
        {
            var client = new FakeClient { SupportsSessionHistory = supported };
            client.Providers.Add(Provider("a"));
            Func<bool, CancellationToken, Task<AuthenticationSessionHistoryResult>> set = null;
            if (settable) set = (unified, token) => throw new AssertionException("Must not change settings.");
            using (var form = new AuthenticationCenterForm(client, setSessionHistory: set) { ShowInTaskbar = false })
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var toggle = FormTestDriver.Field<CheckBox>(form, "_sessionHistoryCheckBox");
                Assert.That(toggle.Visible, Is.EqualTo(offered));
                Assert.That(toggle.AccessibleName, Is.Not.Empty);
                Assert.That(toggle.Enabled, Is.EqualTo(offered));
                Assert.That(FormTestDriver.Field<Button>(form, "_sessionHistoryRetryButton").Visible, Is.False);
                Assert.That(client.SessionHistoryReads, Is.EqualTo(offered ? 1 : 0));
            }
        }

        [Test]
        public async Task EnablingSessionHistoryExplainsHowManySessionsMoveAndMovesThem()
        {
            var client = new FakeClient { SupportsSessionHistory = true, Preview = new AuthenticationSessionHistoryPreview { ToMigrate = 3 } };
            client.Providers.Add(Provider("a"));
            var sets = new List<bool>();
            string explained = null;
            using (var form = new AuthenticationCenterForm(client, setSessionHistory: (unified, token) =>
            {
                sets.Add(unified);
                client.SessionHistory = new AuthenticationSessionHistoryState { Unified = unified };
                return Task.FromResult(new AuthenticationSessionHistoryResult
                {
                    State = client.SessionHistory, GlobalApplied = true, Reconnected = true,
                    Moved = new AuthenticationSessionHistoryMove { Sessions = 3, IndexRows = 3 }
                });
            }) { ShowInTaskbar = false })
            {
                form.ConfirmSessionHistory = (title, message) =>
                {
                    explained = message;
                    return true;
                };
                form.Show();
                await form.RefreshAsync();
                var toggle = FormTestDriver.Field<CheckBox>(form, "_sessionHistoryCheckBox");
                var retry = FormTestDriver.Field<Button>(form, "_sessionHistoryRetryButton");
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                Assert.That(toggle.Checked, Is.False);
                Assert.That(list.TabIndex, Is.LessThan(toggle.Parent.TabIndex));
                Assert.That(toggle.Parent.TabIndex, Is.LessThan(((Control)form.CancelButton).Parent.TabIndex));

                await form.ToggleSessionHistoryAsync();
                Assert.That(explained, Does.Contain("3"));
                Assert.That(sets, Is.EqualTo(new[] { true }));
                Assert.That(toggle.Checked, Is.True);
                Assert.That(toggle.Enabled, Is.True);
                Assert.That(retry.Visible, Is.False);
                Assert.That(FormTestDriver.Field<Label>(form, "_statusLabel").Text, Does.Contain("3"));
            }
        }

        [Test]
        public async Task CancellingSessionHistoryConfirmationChangesNothingAndAnUnfinishedMoveCanBeRetried()
        {
            var client = new FakeClient
            {
                SupportsSessionHistory = true,
                SessionHistory = new AuthenticationSessionHistoryState { Unified = true },
                Preview = new AuthenticationSessionHistoryPreview()
            };
            client.Providers.Add(Provider("a"));
            var sets = new List<bool>();
            var answer = false;
            string explained = null;
            using (var form = new AuthenticationCenterForm(client, setSessionHistory: (unified, token) =>
            {
                sets.Add(unified);
                var failed = sets.Count == 1;
                client.SessionHistory = new AuthenticationSessionHistoryState { Unified = unified, Pending = failed };
                return Task.FromResult(new AuthenticationSessionHistoryResult
                {
                    State = client.SessionHistory, GlobalApplied = true, Reconnected = true,
                    Moved = failed
                        ? new AuthenticationSessionHistoryMove { Error = "history_move_failed" }
                        : new AuthenticationSessionHistoryMove { Sessions = 2, IndexRows = 2 }
                });
            }) { ShowInTaskbar = false })
            {
                form.ConfirmSessionHistory = (title, message) =>
                {
                    explained = message;
                    return answer;
                };
                form.Show();
                await form.RefreshAsync();
                var toggle = FormTestDriver.Field<CheckBox>(form, "_sessionHistoryCheckBox");
                var retry = FormTestDriver.Field<Button>(form, "_sessionHistoryRetryButton");
                Assert.That(toggle.Checked, Is.True);

                await form.ToggleSessionHistoryAsync();
                Assert.That(sets, Is.Empty);
                Assert.That(toggle.Checked, Is.True);
                // Without sessions to move, the explanation names no count.
                Assert.That(explained, Does.Not.Contain("0"));

                answer = true;
                await form.ToggleSessionHistoryAsync();
                Assert.That(sets, Is.EqualTo(new[] { false }));
                Assert.That(toggle.Checked, Is.False);
                Assert.That(retry.Visible, Is.True);
                Assert.That(retry.Enabled, Is.True);
                Assert.That(retry.AccessibleName, Is.Not.Empty);

                await form.RetrySessionHistoryAsync();
                Assert.That(sets, Is.EqualTo(new[] { false, false }));
                Assert.That(retry.Visible, Is.False);
            }
        }

        [Test]
        public async Task UnreadableSessionHistoryLeavesProvidersUsable()
        {
            var client = new FakeClient { SupportsSessionHistory = true, SessionHistoryFailure = new AuthenticationCenterException("backend_unavailable") };
            client.Providers.Add(Provider("a"));
            using (var form = new AuthenticationCenterForm(client, setSessionHistory: (unified, token) => throw new AssertionException("Must not change settings."))
            { ShowInTaskbar = false })
            {
                form.Show();
                await form.RefreshAsync();
                Assert.That(FormTestDriver.Field<ListView>(form, "_providersList").Items.Count, Is.EqualTo(1));
                Assert.That(FormTestDriver.Field<CheckBox>(form, "_sessionHistoryCheckBox").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Label>(form, "_statusLabel").Text, Is.Not.Empty);
            }
        }

        [Test]
        public void InitialLoadKeepsFocusOnProviderListWhileWaitingForBackend()
        {
            var reply = new TaskCompletionSource<AuthenticationCenterSnapshot>();
            var client = new FakeClient { ReadReply = reply };
            using (var form = new AuthenticationCenterForm(client) { ShowInTaskbar = false })
            {
                var closeFocusCount = 0;
                ((Control)form.CancelButton).GotFocus += (sender, args) => closeFocusCount++;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                Assert.That(client.ReadCalls, Is.EqualTo(1));
                Assert.That(list.Focused, Is.True);
                Assert.That(closeFocusCount, Is.Zero);
                reply.SetResult(new AuthenticationCenterSnapshot { Providers = new List<AuthenticationProvider> { Provider("a") } });
                System.Windows.Forms.Application.DoEvents();
                Assert.That(list.Focused, Is.True);
                Assert.That(list.SelectedItems.Count, Is.EqualTo(1));
                Assert.That(closeFocusCount, Is.Zero);
            }
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void EnablingDoesNotMoveFocusOrStealItAfterUserTabsAway(bool reconnected, bool moveFocus)
        {
            var reply = new TaskCompletionSource<AuthenticationApplyResult>();
            var client = new FakeClient();
            client.Providers.Add(Provider("a"));
            using (var form = new AuthenticationCenterForm(client, apply: (provider, token) => reply.Task) { ShowInTaskbar = false })
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                list.Focus();
                var close = (Control)form.CancelButton;
                var closeFocusCount = 0;
                close.GotFocus += (sender, args) => closeFocusCount++;
                var applying = form.ApplySelectedAsync();
                Assert.That(applying.IsCompleted, Is.False);
                Assert.That(list.Focused, Is.True);
                Assert.That(closeFocusCount, Is.Zero);
                if (moveFocus) close.Focus();
                reply.SetResult(new AuthenticationApplyResult { GlobalApplied = true, Reconnected = reconnected });
                applying.GetAwaiter().GetResult();
                Assert.That(moveFocus ? close.Focused : list.Focused, Is.True);
                Assert.That(closeFocusCount, Is.EqualTo(moveFocus ? 1 : 0));
            }
        }

        [TestCase("zh-CN", "全局配置已应用", "已启用")]
        [TestCase("en-US", "Global configuration applied", "enabled")]
        public void EnterEnablesSelectedProviderWithReadableStatusWithoutEditing(string culture, string expectedStatus, string expectedSpeech)
        {
            var previousCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                var client = new FakeClient { RejectEditing = true };
                client.Providers.Add(Provider("a"));
                var reader = new Reader();
                var applies = 0;
                using (var form = new AuthenticationCenterForm(client, reader, (provider, token) =>
                {
                    applies++;
                    Assert.That(provider.Id, Is.EqualTo("a"));
                    client.Current = provider.Id;
                    return Task.FromResult(new AuthenticationApplyResult { GlobalApplied = true, Reconnected = true });
                }) { ShowInTaskbar = false })
                {
                    form.Show();
                    form.RefreshAsync().GetAwaiter().GetResult();
                    var list = FormTestDriver.Field<ListView>(form, "_providersList");
                    list.Items[0].Selected = list.Items[0].Focused = true;
                    list.Focus();
                    Assert.That(applies, Is.Zero);
                    Assert.That(reader.SpeakCalls, Is.Zero);
                    var message = Message.Create(list.Handle, 0x100, (IntPtr)Keys.Enter, IntPtr.Zero);
                    Assert.That(list.PreProcessControlMessage(ref message), Is.EqualTo(PreProcessControlState.MessageNeeded));
                    var modifiedKey = new KeyEventArgs(Keys.Control | Keys.Enter);
                    typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(list, new object[] { modifiedKey });
                    Assert.That(applies, Is.Zero);
                    var args = new KeyEventArgs(Keys.Enter);
                    typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(list, new object[] { args });
                    Assert.That(args.Handled && args.SuppressKeyPress, Is.True);
                    Assert.That(applies, Is.EqualTo(1));
                    Assert.That(client.ReadForEditCalls, Is.Zero);
                    Assert.That(reader.SpeakCalls, Is.EqualTo(1), "A successful switch is the one result the authentication center speaks.");
                    Assert.That(reader.LastSpokenText, Does.Contain("a").And.Contain(expectedSpeech));
                    Assert.That(FormTestDriver.Field<Label>(form, "_statusLabel").Text, Does.StartWith(expectedStatus));
                    Assert.That(list.SelectedItems[0].Tag, Is.SameAs(client.Providers[0]));
                    Assert.That(list.Focused, Is.True);
                }
            }
            finally { Thread.CurrentThread.CurrentUICulture = previousCulture; }
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        public void ApplyWaitsForConfirmedSuccessAndIgnoresRepeatedRequests(bool globalApplied, bool reconnected)
        {
            var client = new FakeClient();
            client.Providers.Add(Provider("a"));
            var reader = new Reader();
            var reply = new TaskCompletionSource<AuthenticationApplyResult>();
            var applies = 0;
            using (var form = new AuthenticationCenterForm(client, reader, (provider, token) =>
            {
                applies++;
                return reply.Task;
            }))
            {
                var handle = form.Handle;
                form.RefreshAsync().GetAwaiter().GetResult();
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                var listHandle = list.Handle;
                list.Items[0].Selected = true;
                ErrorBoxRecorder.Clear();
                var applying = form.ApplySelectedAsync();
                form.ApplySelectedAsync().GetAwaiter().GetResult();
                Assert.That(applies, Is.EqualTo(1));
                Assert.That(reader.SpeakCalls, Is.Zero);
                reply.SetResult(new AuthenticationApplyResult { GlobalApplied = globalApplied, Reconnected = reconnected });
                applying.GetAwaiter().GetResult();
                var succeeded = globalApplied && reconnected;
                Assert.That(reader.SpeakCalls, Is.EqualTo(succeeded ? 1 : 0), "Only a confirmed switch is spoken.");
                Assert.That(ErrorBoxRecorder.Messages, Has.Count.EqualTo(succeeded ? 0 : 1), "A failed or unconfirmed switch opens one error box.");
                if (!succeeded)
                    Assert.That(ErrorBoxRecorder.Messages[0], Is.EqualTo(FormTestDriver.Field<Label>(form, "_statusLabel").Text));
            }
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ProviderSaveKeepsSelectionAndWaitsForExplicitEnable(bool listed, bool recovery)
        {
            var client = new FakeClient();
            client.Providers.Add(Provider("other"));
            var saved = Provider("saved");
            saved.RecoveryRequired = recovery;
            if (listed) client.Providers.Add(saved);
            var reader = new Reader();
            var applies = 0;
            using (var form = new AuthenticationCenterForm(client, reader, (provider, token) =>
            {
                applies++;
                Assert.That(provider.Id, Is.EqualTo(saved.Id));
                client.Current = provider.Id;
                return Task.FromResult(new AuthenticationApplyResult { GlobalApplied = true, Reconnected = true });
            }))
            {
                var handle = form.Handle;
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                var listHandle = list.Handle;
                form.CompleteProviderSaveAsync(new AuthenticationSaveResult { Provider = saved, Reconnected = true }).GetAwaiter().GetResult();
                Assert.That(applies, Is.Zero, "Saving must not enable a provider or ask to replace the global configuration.");
                Assert.That(reader.SpeakCalls, Is.Zero);
                Assert.That(FormTestDriver.Field<Label>(form, "_statusLabel").Text, Is.Not.Empty);
                Assert.That(list.SelectedItems[0].Tag,
                    Is.SameAs(listed ? saved : client.Providers[0]));
                if (listed)
                {
                    form.ApplySelectedAsync().GetAwaiter().GetResult();
                    Assert.That(applies, Is.EqualTo(1));
                    Assert.That(client.Current, Is.EqualTo(saved.Id));
                    Assert.That(reader.SpeakCalls, Is.EqualTo(1), "Only the confirmed switch is spoken, not the save.");
                }
            }
        }

        [Test]
        public void EnterDoesNotEnableAProviderWithoutCredentials()
        {
            var client = new FakeClient { RejectEditing = true };
            var provider = Provider("a");
            provider.HasApiKey = false;
            client.Providers.Add(provider);
            var reader = new Reader();
            var applies = 0;
            using (var form = new AuthenticationCenterForm(client, reader, (selected, token) =>
            {
                applies++;
                return Task.FromResult(new AuthenticationApplyResult { GlobalApplied = true, Reconnected = true });
            }))
            {
                var handle = form.Handle;
                form.RefreshAsync().GetAwaiter().GetResult();
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                var listHandle = list.Handle;
                list.Items[0].Selected = true;
                typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(list, new object[] { new KeyEventArgs(Keys.Enter) });
                Assert.That(applies, Is.Zero);
                Assert.That(client.ReadForEditCalls, Is.Zero);
                Assert.That(reader.SpeakCalls, Is.Zero);
            }
        }

        [Test]
        public async Task OutdatedTargetOffersAnAccessibleUpdateAndRefreshesAfterSuccess()
        {
            var client = new FakeClient { Failure = new AuthenticationCenterException("backend_update_available") };
            var reader = new Reader();
            var updates = 0;
            using (var form = new AuthenticationCenterForm(client, reader,
                updateBackend: token =>
                {
                    updates++;
                    client.Failure = null;
                    return Task.CompletedTask;
                }))
            {
                form.Show();
                await form.RefreshAsync();
                var button = FormTestDriver.Field<Button>(form, "_updateBackendButton");
                Assert.That(button.Visible && button.Enabled, Is.True,
                    $"visible={button.Visible}, enabled={button.Enabled}, status={FormTestDriver.Field<Label>(form, "_statusLabel").Text}");
                Assert.That(button.AccessibleName, Is.Not.Empty);
                Assert.That(button.TabStop, Is.True);
                await form.UpdateBackendAsync();
                Assert.That(updates, Is.EqualTo(1));
                Assert.That(button.Visible, Is.False);
                Assert.That(client.ReadCalls, Is.EqualTo(2));
                Assert.That(reader.SpeakCalls, Is.Zero);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RouteHealthIsVisibleWithoutSpeakingOrApplying(bool running)
        {
            var client = new FakeClient { RouteRequired = true, RouterRunning = running };
            var reader = new Reader();
            using (var form = new AuthenticationCenterForm(client, reader))
            {
                var handle = form.Handle;
                form.RefreshAsync().GetAwaiter().GetResult();
                var message = FormTestDriver.Field<Label>(form, "_statusLabel").Text;
                Assert.That(message, Is.Not.Empty);
                Assert.That(reader.SpeakCalls, Is.Zero);
                if (running) Assert.That(message, Does.Contain("3"));
                Assert.That(client.SaveCalls, Is.Zero);
                Assert.That(client.ReadCalls, Is.EqualTo(1));
            }
        }

        [Test]
        public void Controls_AreNamedResizableAndContainersDoNotReceiveTabFocus()
        {
            using (var center = new AuthenticationCenterForm(new FakeClient()))
            using (var editor = new AuthenticationProviderEditForm(new FakeClient()))
            {
                foreach (var form in new Form[] { center, editor })
                {
                    Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                    Assert.That(form.MinimumSize.Width, Is.GreaterThan(0));
                    foreach (var control in Descendants(form))
                    {
                        if (control is Panel || control is Label)
                        {
                            Assert.That(control.TabStop, Is.False);
                        }
                        if (control is Button || control is TextBox || control is ComboBox || control is CheckBox || control is ListView)
                        {
                            Assert.That(control.AccessibleName, Is.Not.Null.And.Not.Empty, control.GetType().Name);
                        }
                    }
                }
                Assert.That(FormTestDriver.Field<Button>(center, "_refreshButton").TabStop, Is.False);
                Assert.That(FormTestDriver.Field<TextBox>(editor, "_keyTextBox").UseSystemPasswordChar, Is.True);
                Assert.That(Descendants(center).OfType<Button>().Any(button => button.DialogResult == DialogResult.Cancel), Is.True);
                Assert.That(FormTestDriver.Field<TextBox>(editor, "_nameTextBox").TabIndex, Is.LessThan(FormTestDriver.Field<TextBox>(editor, "_keyTextBox").TabIndex));
            }
        }

        [Test]
        public void LoginAndModelValidationKeepStatusWithoutSpeaking()
        {
            var reader = new Reader();
            using (var login = new AuthenticationLoginControl(new FakeClient(), reader, null, code => { }, url => { }))
            using (var model = new AuthenticationModelEditForm(null, reader))
            {
                login.StartLoginAsync().GetAwaiter().GetResult();
                Assert.That(FormTestDriver.Field<TextBox>(login, "_status").Text, Is.Not.Empty);
                Assert.That(model.Save(), Is.False);
                Assert.That(FormTestDriver.Field<Label>(model, "_status").Text, Is.Not.Empty);
                Assert.That(reader.SpeakCalls, Is.Zero);
            }
        }

        [Test]
        public void SelectionDoesNotPerformIoAndCurrentProviderCannotBeDeleted()
        {
            var client = new FakeClient();
            client.Providers.Add(Provider("a"));
            client.Current = "a";
            using (var form = new AuthenticationCenterForm(client))
            {
                var handle = form.Handle;
                form.RefreshAsync().GetAwaiter().GetResult();
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                var listHandle = list.Handle;
                list.Items[0].Selected = true;
                Assert.That(FormTestDriver.Field<MenuItem>(form, "_editMenuItem").Enabled, Is.True);
                Assert.That(FormTestDriver.Field<MenuItem>(form, "_deleteMenuItem").Enabled, Is.False);
                Assert.That(client.ReadCalls, Is.EqualTo(1));
                Assert.That(client.SaveCalls, Is.Zero);
                Assert.That(client.CopyCalls, Is.Zero);
                Assert.That(client.DeleteCalls, Is.Zero);
            }
        }

        [Test]
        public void SavedChatGptAccountCanBeEnabledWithoutAnApiKey()
        {
            var client = new FakeClient();
            var account = Provider("account");
            account.Kind = "chatgpt";
            account.HasApiKey = false;
            account.AccountId = "archived-account";
            account.AccountLabel = "Test account";
            client.Providers.Add(account);
            var applies = 0;
            using (var form = new AuthenticationCenterForm(client, null,
                (provider, token) =>
                {
                    applies++;
                    Assert.That(provider, Is.SameAs(account));
                    return Task.FromResult(new AuthenticationApplyResult { GlobalApplied = true, Reconnected = true, OperationId = "operation" });
                }))
            {
                var handle = form.Handle;
                form.RefreshAsync().GetAwaiter().GetResult();
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                var listHandle = list.Handle;
                list.Items[0].Selected = true;
                Assert.That(FormTestDriver.Field<MenuItem>(form, "_enableMenuItem").Enabled, Is.True);
                form.ApplySelectedAsync().GetAwaiter().GetResult();
                Assert.That(applies, Is.EqualTo(1));
            }
        }

        [TestCase("chatgpt", false, "account", true, false)]
        [TestCase("chatgpt", false, null, false, false)]
        [TestCase("chat_completions", true, null, false, true)]
        [TestCase("chat_completions", false, null, false, false)]
        [TestCase("anthropic", true, null, false, true)]
        public void EnableMenuMatchesSupportedProviderAndCredentialState(string kind, bool hasApiKey,
            string accountId, bool needsReauthentication, bool expected)
        {
            var client = new FakeClient();
            var provider = Provider("selection");
            provider.Kind = kind;
            provider.HasApiKey = hasApiKey;
            provider.AccountId = accountId;
            provider.NeedsReauthentication = needsReauthentication;
            client.Providers.Add(provider);
            using (var form = new AuthenticationCenterForm(client, null,
                (selected, token) => Task.FromResult(new AuthenticationApplyResult { GlobalApplied = true, Reconnected = true })))
            {
                var handle = form.Handle;
                form.RefreshAsync().GetAwaiter().GetResult();
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                var listHandle = list.Handle;
                list.Items[0].Selected = true;
                Assert.That(FormTestDriver.Field<MenuItem>(form, "_enableMenuItem").Enabled, Is.EqualTo(expected));
            }
        }

        [Test]
        public void InitialLoadSelectsCurrentAndChatGptOffersReauthenticationWithoutApplying()
        {
            var client = new FakeClient { Current = "b" };
            client.Providers.Add(Provider("a"));
            var account = Provider("b");
            account.Kind = "chatgpt";
            account.AccountLabel = "Test account";
            account.NeedsReauthentication = true;
            client.Providers.Add(account);
            using (var form = new AuthenticationCenterForm(client))
            {
                var handle = form.Handle;
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                var listHandle = list.Handle;
                form.RefreshAsync().GetAwaiter().GetResult();
                Assert.That(list.SelectedItems.Count, Is.EqualTo(1));
                Assert.That(list.SelectedItems[0].Tag, Is.SameAs(account));
                Assert.That(list.SelectedItems[0].SubItems[5].Text, Is.EqualTo("Test account"));
                Assert.That(FormTestDriver.Field<MenuItem>(form, "_reauthenticateMenuItem").Enabled, Is.True);
                Assert.That(FormTestDriver.Field<MenuItem>(form, "_enableMenuItem").Enabled, Is.False);
                Assert.That(client.SaveCalls, Is.Zero);
            }
        }

        [Test]
        public void ProviderSelectionFiltersLocallyAndRequiresExplicitConfirmation()
        {
            var presets = new[]
            {
                new AuthenticationProviderPreset { Id = "custom", Name = "Custom provider", Kind = "responses", Available = true, AllowsProtocolSelection = true },
                new AuthenticationProviderPreset { Id = "chatgpt", Name = "ChatGPT", Kind = "chatgpt", Available = true },
                new AuthenticationProviderPreset { Id = "xai", Name = "xAI OAuth", Kind = "responses", Available = false }
            };
            using (var form = new AuthenticationProviderSelectionForm(presets))
            {
                form.ShowInTaskbar = false;
                form.Show();
                var search = FormTestDriver.Field<TextBox>(form, "_searchTextBox");
                var list = FormTestDriver.Field<ListBox>(form, "_providersList");
                var next = FormTestDriver.Field<Button>(form, "_nextButton");
                Assert.That(list.Items.Count, Is.GreaterThanOrEqualTo(3));
                search.Text = "custom";
                Assert.That(list.Items.Count, Is.EqualTo(1));
                Assert.That(form.SelectedTemplate, Is.Null);
                Assert.That(next.Enabled, Is.True);
                next.PerformClick();
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK));
                Assert.That(form.SelectedTemplate.Kind, Is.EqualTo("responses"));
            }
            using (var form = new AuthenticationProviderSelectionForm(presets))
            {
                var search = FormTestDriver.Field<TextBox>(form, "_searchTextBox");
                var next = FormTestDriver.Field<Button>(form, "_nextButton");
                search.Text = "chatgpt";
                Assert.That(next.Enabled, Is.True);
                search.Text = "xai";
                Assert.That(next.Enabled, Is.False);
                Assert.That(form.SelectedTemplate, Is.Null);
            }
        }

        [TestCase("zh-CN", "ChatGPT 账号授权", "智谱 GLM")]
        [TestCase("en-US", "ChatGPT account authorization", "Zhipu GLM")]
        public void ProviderSelectionSortsAndLocalizesWithoutChangingTemplates(string culture, string chatGptName, string zhipuName)
        {
            var previousCulture = Thread.CurrentThread.CurrentUICulture;
            var presets = new[]
            {
                new AuthenticationProviderPreset { Id = "cc-switch-999", Name = "Zhipu GLM", Available = true, BaseUrl = "https://example.invalid/", Model = "original-model" },
                new AuthenticationProviderPreset { Id = "official_api", Name = "OpenAI API", Available = true },
                new AuthenticationProviderPreset { Id = "custom", Name = "Custom", Available = true },
                new AuthenticationProviderPreset { Id = "cc-switch-2", Name = "aNew upstream brand", Available = true },
                new AuthenticationProviderPreset { Id = "chatgpt", Name = "ChatGPT", Available = true },
                new AuthenticationProviderPreset { Id = "cc-switch-1", Name = "Kimi Global", Available = true }
            };
            var originalNames = presets.Select(p => p.Name).ToArray();
            try
            {
                Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(culture);
                using (var form = new AuthenticationProviderSelectionForm(presets))
                {
                    form.ShowInTaskbar = false;
                    form.Show();
                    var list = FormTestDriver.Field<ListBox>(form, "_providersList");
                    var search = FormTestDriver.Field<TextBox>(form, "_searchTextBox");
                    Assert.That(list.Items.Cast<AuthenticationProviderPreset>().Select(p => p.Id),
                        Is.EqualTo(new[] { "chatgpt", "custom", "official_api", "cc-switch-2", "cc-switch-1", "cc-switch-999" }));
                    Assert.That(list.GetItemText(list.Items[0]), Is.EqualTo(chatGptName));
                    Assert.That(list.GetItemText(list.Items[3]), Is.EqualTo("aNew upstream brand"));
                    Assert.That(list.GetItemText(list.Items[5]), Is.EqualTo(zhipuName));
                    foreach (var query in new[] { zhipuName, "zhipu", "cc-switch-999" })
                    {
                        search.Text = query;
                        Assert.That(list.Items.Count, Is.EqualTo(1));
                        Assert.That(list.Items[0], Is.SameAs(presets[0]));
                        Assert.That(search.Focused, Is.True, "Filtering must not steal focus.");
                        Assert.That(form.SelectedTemplate, Is.Null);
                    }
                    FormTestDriver.Field<Button>(form, "_nextButton").PerformClick();
                    Assert.That(form.SelectedTemplate, Is.SameAs(presets[0]));
                    Assert.That(form.SelectedTemplate.BaseUrl, Is.EqualTo("https://example.invalid/"));
                    Assert.That(form.SelectedTemplate.Model, Is.EqualTo("original-model"));
                }
                Assert.That(presets.Select(p => p.Name), Is.EqualTo(originalNames));
            }
            finally { Thread.CurrentThread.CurrentUICulture = previousCulture; }
        }

        [Test]
        public void ProviderListUsesNativeContextMenuWithoutSelectionSideEffects()
        {
            var client = new FakeClient();
            client.Providers.Add(Provider("a"));
            using (var form = new AuthenticationCenterForm(client))
            {
                form.RefreshAsync().GetAwaiter().GetResult();
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                list.Items[0].Selected = true;
                Assert.That(list.ContextMenu, Is.Not.Null);
                Assert.That(list.ContextMenu.MenuItems[0].Enabled, Is.False, "No apply callback is available.");
                Assert.That(client.ReadCalls, Is.EqualTo(1));
                Assert.That(client.SaveCalls, Is.Zero);
            }
        }

        [Test]
        public void SavingAnAddedProviderSendsDraftAndReopeningReadsItBack()
        {
            var client = new FakeClient();
            var reader = new Reader();
            using (var editor = new AuthenticationProviderEditForm(client, null, reader, save: async (edit, token) => new AuthenticationSaveResult { Provider = await client.SaveAsync(edit, token), Reconnected = true }))
            {
                FormTestDriver.Field<TextBox>(editor, "_nameTextBox").Text = "New provider";
                FormTestDriver.Field<AuthenticationModelsControl>(editor, "_models").DefaultModel = "test-model";
                FormTestDriver.Field<TextBox>(editor, "_keyTextBox").Text = "synthetic-private-key";
                editor.SaveAsync().GetAwaiter().GetResult();
                Assert.That(editor.DialogResult, Is.EqualTo(DialogResult.OK));
                Assert.That(client.LastEdit.ApiKey, Is.EqualTo("synthetic-private-key"));
                Assert.That(client.LastEdit.ExpectedVersion, Is.Null);
                Assert.That(FormTestDriver.Field<TextBox>(editor, "_keyTextBox").Text, Is.Empty);
                Assert.That(FormTestDriver.Field<TextBox>(editor, "_statusLabel").Text, Does.Not.Contain("synthetic-private-key"));
                Assert.That(reader.SpeakCalls, Is.Zero);
                Assert.That(client.Current, Is.Null);
            }
            using (var reopened = new AuthenticationCenterForm(client, reader))
            {
                reopened.RefreshAsync().GetAwaiter().GetResult();
                Assert.That(FormTestDriver.Field<ListView>(reopened, "_providersList").Items.Count, Is.EqualTo(1));
                Assert.That(FormTestDriver.Field<Label>(reopened, "_statusLabel").Text, Is.Not.Empty);
                Assert.That(reader.SpeakCalls, Is.Zero);
            }
        }

        [Test]
        public void EditingRetainsKeyUnlessExplicitlyCleared()
        {
            var client = new FakeClient();
            var provider = Provider("a");
            using (var editor = new AuthenticationProviderEditForm(client, provider, save: async (edit, token) => new AuthenticationSaveResult { Provider = await client.SaveAsync(edit, token), Reconnected = true }))
            {
                editor.SaveAsync().GetAwaiter().GetResult();
                Assert.That(client.LastEdit.ApiKey, Is.Null);
                Assert.That(client.LastEdit.ClearApiKey, Is.False);
                Assert.That(client.LastEdit.ExpectedVersion, Is.EqualTo(provider.Version));
            }
            using (var editor = new AuthenticationProviderEditForm(client, provider, save: async (edit, token) => new AuthenticationSaveResult { Provider = await client.SaveAsync(edit, token), Reconnected = true }))
            {
                FormTestDriver.Field<TextBox>(editor, "_keyTextBox").Text = "discarded-key";
                FormTestDriver.Field<CheckBox>(editor, "_clearKeyCheckBox").Checked = true;
                Assert.That(FormTestDriver.Field<TextBox>(editor, "_keyTextBox").Text, Is.Empty);
                Assert.That(FormTestDriver.Field<TextBox>(editor, "_keyTextBox").Enabled, Is.False);
                editor.SaveAsync().GetAwaiter().GetResult();
                Assert.That(client.LastEdit.ClearApiKey, Is.True);
                Assert.That(client.LastEdit.ApiKey, Is.Null);
            }
        }

        [Test]
        public void ReadAndSaveFailuresAreVisibleWithoutSpeakingOrExceptionPayloads()
        {
            var client = new FakeClient { Failure = new Exception("sensitive-provider-payload") };
            var reader = new Reader();
            using (var form = new AuthenticationCenterForm(client, reader))
            {
                form.RefreshAsync().GetAwaiter().GetResult();
                Assert.That(FormTestDriver.Field<Button>(form, "_addButton").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Button>(form, "_refreshButton").Enabled, Is.True);
                Assert.That(FormTestDriver.Field<Label>(form, "_statusLabel").Text, Is.Not.Empty);
                Assert.That(FormTestDriver.Field<Label>(form, "_statusLabel").Text, Does.Not.Contain("sensitive-provider-payload"));
                Assert.That(reader.SpeakCalls, Is.Zero);
            }
            using (var editor = new AuthenticationProviderEditForm(client, Provider("a"), reader, save: async (edit, token) => new AuthenticationSaveResult { Provider = await client.SaveAsync(edit, token), Reconnected = true }))
            {
                editor.SaveAsync().GetAwaiter().GetResult();
                Assert.That(editor.DialogResult, Is.Not.EqualTo(DialogResult.OK));
                Assert.That(FormTestDriver.Field<Button>(editor, "_saveButton").Enabled, Is.True);
                Assert.That(FormTestDriver.Field<TextBox>(editor, "_statusLabel").Text, Does.Not.Contain("sensitive-provider-payload"));
                Assert.That(reader.SpeakCalls, Is.Zero);
            }
        }

        [Test]
        public void CopyUsesBackendOperationAndRefreshesWithoutExposingKey()
        {
            var client = new FakeClient();
            client.Providers.Add(Provider("a"));
            using (var form = new AuthenticationCenterForm(client))
            {
                var handle = form.Handle;
                form.RefreshAsync().GetAwaiter().GetResult();
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                var listHandle = list.Handle;
                list.Items[0].Selected = true;
                form.CopySelectedAsync().GetAwaiter().GetResult();
                Assert.That(client.CopyCalls, Is.EqualTo(1));
                Assert.That(client.SaveCalls, Is.Zero);
                Assert.That(list.Items.Count, Is.EqualTo(2));
            }
        }

        [Test]
        public void LoadErrorsOpenOneErrorBoxUntilTheyChangeOrTheUserRefreshes()
        {
            var client = new FakeClient { SyncError = "database_failed" };
            client.Providers.Add(Provider("a"));
            var reader = new Reader();
            using (var form = new AuthenticationCenterForm(client, reader) { ShowInTaskbar = false })
            {
                form.Show();
                ErrorBoxRecorder.Clear();
                form.RefreshAsync().GetAwaiter().GetResult();
                var status = FormTestDriver.Field<Label>(form, "_statusLabel");
                Assert.That(ErrorBoxRecorder.Messages, Has.Count.EqualTo(1));
                Assert.That(status.Text, Does.Contain(ErrorBoxRecorder.Messages[0]), "The error stays readable in the status line.");
                form.RefreshAsync().GetAwaiter().GetResult();
                Assert.That(ErrorBoxRecorder.Messages, Has.Count.EqualTo(1), "Refreshes after other operations do not repeat an unchanged error.");
                FormTestDriver.Field<Button>(form, "_refreshButton").PerformClick();
                Assert.That(ErrorBoxRecorder.Messages, Has.Count.EqualTo(2), "An explicit refresh reports the error again.");
                client.SyncError = null;
                form.RefreshAsync().GetAwaiter().GetResult();
                Assert.That(ErrorBoxRecorder.Messages, Has.Count.EqualTo(2));
                client.Failure = new AuthenticationCenterException("backend_update_available");
                form.RefreshAsync().GetAwaiter().GetResult();
                Assert.That(ErrorBoxRecorder.Messages, Has.Count.EqualTo(2), "An available backend update is not an error.");
                client.Failure = new AuthenticationCenterException("database_failed");
                form.RefreshAsync().GetAwaiter().GetResult();
                Assert.That(ErrorBoxRecorder.Messages, Has.Count.EqualTo(3));
                Assert.That(ErrorBoxRecorder.Messages[2], Is.EqualTo(status.Text));
                Assert.That(reader.SpeakCalls, Is.Zero, "Errors use the message box, not speech.");
            }
        }

        private static AuthenticationProvider Provider(string id)
        {
            return new AuthenticationProvider { Id = id, Name = id, Kind = "responses", BaseUrl = "https://example.test/v1", Model = "test", Version = "v1", HasApiKey = true };
        }

        [Test]
        public void AppliedGlobalStateWithFailedReconnectOffersConnectionOnlyRetry()
        {
            var client = new FakeClient();
            client.Providers.Add(Provider("a"));
            var applies = 0;
            var reconnects = 0;
            using (var form = new AuthenticationCenterForm(client, null,
                (provider, token) =>
                {
                    applies++;
                    return Task.FromResult(new AuthenticationApplyResult { GlobalApplied = true, Reconnected = false, OperationId = "operation" });
                }, token =>
                {
                    reconnects++;
                    return Task.FromResult(true);
                }, () => "official@example.test"))
            {
                var handle = form.Handle;
                form.RefreshAsync().GetAwaiter().GetResult();
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                var listHandle = list.Handle;
                list.Items[0].Selected = true;
                Assert.That(applies, Is.Zero);
                form.ApplySelectedAsync().GetAwaiter().GetResult();
                Assert.That(applies, Is.EqualTo(1));
                Assert.That(FormTestDriver.Field<Button>(form, "_retryButton").Enabled, Is.True);
                Assert.That(FormTestDriver.Field<MenuItem>(form, "_enableMenuItem").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Label>(form, "_identityLabel").Text, Does.Contain("official@example.test"));
                form.RetryConnectionAsync().GetAwaiter().GetResult();
                Assert.That(reconnects, Is.EqualTo(1));
                Assert.That(applies, Is.EqualTo(1));
                Assert.That(FormTestDriver.Field<Button>(form, "_retryButton").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<MenuItem>(form, "_enableMenuItem").Enabled, Is.True);
            }
        }

        [Test]
        public void BusyApplyIsReportedAndNeverOffersAConnectionRetry()
        {
            var client = new FakeClient();
            client.Providers.Add(Provider("a"));
            var reader = new Reader();
            using (var form = new AuthenticationCenterForm(client, reader,
                (provider, token) =>
                {
                    throw new AuthenticationCenterException("runtime_busy");
                }))
            {
                var handle = form.Handle;
                form.RefreshAsync().GetAwaiter().GetResult();
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                var listHandle = list.Handle;
                list.Items[0].Selected = true;
                form.ApplySelectedAsync().GetAwaiter().GetResult();
                Assert.That(FormTestDriver.Field<Button>(form, "_retryButton").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Label>(form, "_statusLabel").Text, Is.Not.Empty);
                Assert.That(FormTestDriver.Field<Label>(form, "_statusLabel").Text, Does.Not.Contain("runtime_busy"));
                Assert.That(reader.SpeakCalls, Is.Zero);
                Assert.That(client.SaveCalls, Is.Zero);
            }
        }

        [TestCase(true, true, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        public void AggregationIsOfferedOnlyWhenTheBackendSupportsIt(bool supported, bool changeable, bool offered)
        {
            var client = new FakeClient { SupportsAggregation = supported };
            client.Providers.Add(Provider("a"));
            client.Stack = new AuthenticationStackState { Providers = new List<AuthenticationStackEligibility> { new AuthenticationStackEligibility { ProviderId = "a" } } };
            Func<Func<CancellationToken, Task<AuthenticationStackState>>, CancellationToken, Task<AuthenticationStackResult>> change = null;
            if (changeable) change = (run, token) => throw new AssertionException("Must not change aggregation.");
            using (var form = new AuthenticationCenterForm(client, changeStack: change) { ShowInTaskbar = false })
            {
                form.Show();
                form.RefreshAsync().GetAwaiter().GetResult();
                var start = FormTestDriver.Field<Button>(form, "_aggregationButton");
                Assert.That(FormTestDriver.Field<FlowLayoutPanel>(form, "_aggregationRow").Visible, Is.EqualTo(offered));
                Assert.That(FormTestDriver.Field<MenuItem>(form, "_aggregationMemberMenuItem").Visible, Is.EqualTo(offered));
                Assert.That(start.AccessibleName, Is.Not.Empty);
                Assert.That(start.Enabled, Is.EqualTo(offered));
                Assert.That(FormTestDriver.Field<Button>(form, "_aggregationCatalogButton").Visible, Is.False);
            }
        }

        [Test]
        public async Task StartingAggregationPublishesTheChosenProvidersAndStoppingKeepsTheList()
        {
            var client = new FakeClient { SupportsAggregation = true, RouterRunning = true };
            client.Providers.Add(Provider("a"));
            client.Providers.Add(Provider("b"));
            client.Stack = new AuthenticationStackState
            {
                Providers = new List<AuthenticationStackEligibility>
                {
                    new AuthenticationStackEligibility { ProviderId = "a" },
                    new AuthenticationStackEligibility { ProviderId = "b" }
                }
            };
            var restarts = 0;
            var reader = new Reader();
            using (var form = new AuthenticationCenterForm(client, reader, changeStack: async (run, token) =>
            {
                var state = await run(token);
                if (state.GlobalApplied) restarts++;
                return new AuthenticationStackResult { State = state, Reconnected = true };
            }) { ShowInTaskbar = false })
            {
                AuthenticationStackState offered = null;
                form.ChooseAggregation = state =>
                {
                    offered = state;
                    return Tuple.Create("a", (IReadOnlyList<string>)new List<string> { "b" });
                };
                form.Show();
                await form.RefreshAsync();
                var list = FormTestDriver.Field<ListView>(form, "_providersList");
                var start = FormTestDriver.Field<Button>(form, "_aggregationButton");
                var member = FormTestDriver.Field<Button>(form, "_aggregationMemberButton");
                var row = FormTestDriver.Field<FlowLayoutPanel>(form, "_aggregationRow");
                Assert.That(list.TabIndex, Is.LessThan(row.TabIndex));
                Assert.That(row.TabIndex, Is.LessThan(((Control)form.CancelButton).Parent.TabIndex));
                var startText = start.Text;

                await form.ToggleAggregationAsync();
                Assert.That(offered, Is.Not.Null);
                Assert.That(client.StackCalls, Is.EqualTo(new[] { "enter:a:b" }));
                Assert.That(restarts, Is.EqualTo(1));
                Assert.That(start.Text, Is.Not.EqualTo(startText));
                Assert.That(list.Items.Cast<ListViewItem>().Select(item => item.SubItems[6].Text),
                    Is.EqualTo(new[] { AggregationText("Aggregation.Role.Default", "Default"), AggregationText("Aggregation.Role.Member", "Added") }));
                Assert.That(FormTestDriver.Field<Label>(form, "_aggregationLabel").Text, Does.Contain("1"));
                Assert.That(FormTestDriver.Field<Label>(form, "_statusLabel").Text, Is.Not.Empty);

                // The default stays listed while aggregating; another member can leave.
                list.Items[0].Selected = true;
                System.Windows.Forms.Application.DoEvents();
                Assert.That(member.Enabled, Is.False);
                list.Items[0].Selected = false;
                list.Items[1].Selected = true;
                System.Windows.Forms.Application.DoEvents();
                Assert.That(member.Enabled, Is.True);

                await form.ToggleAggregationAsync();
                Assert.That(client.StackCalls, Is.EqualTo(new[] { "enter:a:b", "exit" }));
                Assert.That(restarts, Is.EqualTo(2));
                Assert.That(start.Text, Is.EqualTo(startText));
                Assert.That(FormTestDriver.Field<Label>(form, "_aggregationLabel").Text, Does.Contain("2"));
                Assert.That(reader.SpeakCalls, Is.Zero);
            }
        }

        [Test]
        public async Task CancellingTheAggregationDialogChangesNothing()
        {
            var client = new FakeClient { SupportsAggregation = true };
            client.Providers.Add(Provider("a"));
            client.Stack = new AuthenticationStackState { Providers = new List<AuthenticationStackEligibility> { new AuthenticationStackEligibility { ProviderId = "a" } } };
            using (var form = new AuthenticationCenterForm(client, changeStack: (run, token) => throw new AssertionException("Must not change aggregation."))
                { ShowInTaskbar = false })
            {
                form.ChooseAggregation = state => null;
                form.Show();
                await form.RefreshAsync();
                await form.ToggleAggregationAsync();
                Assert.That(client.StackCalls, Is.Empty);
            }
        }

        [Test]
        public void AggregationDialogOffersEligibleDefaultsAndKeepsTheStoredList()
        {
            var state = new AuthenticationStackState
            {
                Members = new List<AuthenticationStackMember> { new AuthenticationStackMember { ProviderId = "c" } },
                Providers = new List<AuthenticationStackEligibility>
                {
                    new AuthenticationStackEligibility { ProviderId = "official", JoinRefusal = "stack_official_cannot_join" },
                    new AuthenticationStackEligibility { ProviderId = "b" },
                    new AuthenticationStackEligibility { ProviderId = "c" },
                    new AuthenticationStackEligibility { ProviderId = "broken", JoinRefusal = "invalid_provider", DefaultRefusal = "invalid_provider" }
                }
            };
            var names = new Dictionary<string, string> { { "official", "ChatGPT" }, { "b", "B" }, { "c", "C" }, { "broken", "Broken" } };
            using (var form = new AuthenticationAggregationForm(state, names, "b") { ShowInTaskbar = false })
            {
                form.Show();
                var defaults = FormTestDriver.Field<ComboBox>(form, "_defaultComboBox");
                var members = FormTestDriver.Field<CheckedListBox>(form, "_membersList");
                Assert.That(defaults.AccessibleName, Is.Not.Empty);
                Assert.That(members.AccessibleName, Is.Not.Empty);
                Assert.That(defaults.TabIndex, Is.LessThan(members.TabIndex));
                Assert.That(form.MinimumSize.Width, Is.GreaterThan(0));
                Assert.That(defaults.Items.Cast<object>().Select(item => item.ToString()), Is.EqualTo(new[] { "ChatGPT", "B", "C" }));
                Assert.That(form.DefaultProviderId, Is.EqualTo("b"));
                Assert.That(members.Items.Cast<object>().Select(item => item.ToString()), Is.EqualTo(new[] { "C" }));
                Assert.That(form.MemberIds, Is.EqualTo(new[] { "c" }));
                defaults.SelectedIndex = 0;
                Assert.That(form.DefaultProviderId, Is.EqualTo("official"));
                Assert.That(members.Items.Cast<object>().Select(item => item.ToString()), Is.EqualTo(new[] { "B", "C" }));
                Assert.That(form.MemberIds, Is.EqualTo(new[] { "c" }));
            }
        }

        private static string AggregationText(string key, string fallback)
        {
            return MyCodex.WinForms.Localization.UiLocalization.Text(key, fallback);
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                yield return control;
                foreach (var child in Descendants(control))
                {
                    yield return child;
                }
            }
        }

        private sealed class FakeClient : IAuthenticationCenterClient
        {
            public Task<AuthenticationDeviceLogin> StartBrowserLoginAsync(string target, string id, CancellationToken token) { throw new NotSupportedException(); }
            public Task<AuthenticationConfigDocument> PreviewProviderConfigAsync(AuthenticationProviderEdit edit, string text, CancellationToken token) { throw new NotSupportedException(); }
            public Task<AuthenticationConfigDocument> ViewProviderConfigAsync(AuthenticationProviderEdit edit, CancellationToken token) { throw new NotSupportedException(); }
            public Task<AuthenticationMcpSnapshot> ReadMcpAsync(CancellationToken token) { throw new NotSupportedException(); }
            public Task SaveMcpAsync(string id, IDictionary<string, object> config, bool enabled, string version, CancellationToken token) { throw new NotSupportedException(); }
            public Task DeleteMcpAsync(string id, string version, CancellationToken token) { throw new NotSupportedException(); }
            public Task<int> ImportMcpAsync(string version, CancellationToken token) { throw new NotSupportedException(); }
            public Task<AuthenticationProvider> ReadForEditAsync(AuthenticationProvider provider, CancellationToken cancellationToken)
            {
                ReadForEditCalls++;
                if (RejectEditing) throw new InvalidOperationException("Editing was not requested.");
                return Task.FromResult(provider);
            }
            internal string PreviewModel = "test";
            internal List<AuthenticationCatalogModel> PreviewModels =
                new List<AuthenticationCatalogModel> { new AuthenticationCatalogModel { Model = "test", DisplayName = "Readable model" } };
            internal string PreviewChange = "added";
            public Task<IReadOnlyList<AuthenticationProviderPreset>> ReadPresetsAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<AuthenticationProviderPreset>>(new List<AuthenticationProviderPreset>());
            }

            public Task<IReadOnlyList<AuthenticationCatalogModel>> FetchModelsAsync(AuthenticationModelFetchRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<AuthenticationCatalogModel>>(new List<AuthenticationCatalogModel>());
            }

            internal readonly List<AuthenticationProvider> Providers = new List<AuthenticationProvider>();
            internal string Current;
            internal int ReadCalls, SaveCalls, CopyCalls, DeleteCalls;
            internal int ReadForEditCalls;
            internal bool RejectEditing;
            internal Exception Failure;
            internal TaskCompletionSource<AuthenticationCenterSnapshot> ReadReply;
            internal AuthenticationProviderEdit LastEdit;
            internal bool RouteRequired, RouterRunning, SupportsSessionHistory, SupportsAggregation, Aggregating;
            internal string SyncError;

            public Task<AuthenticationCenterSnapshot> ReadAsync(CancellationToken cancellationToken)
            {
                ReadCalls++;
                if (Failure != null)
                {
                    throw Failure;
                }
                if (ReadReply != null) return ReadReply.Task;
                return Task.FromResult(new AuthenticationCenterSnapshot { TargetName = "Test", CodexHome = "test-home", CurrentProviderId = Current, Providers = Providers.ToList(), RouteRequired = RouteRequired, RouterRunning = RouterRunning, RouteInFlight = 3, SupportsSessionHistory = SupportsSessionHistory, SupportsAggregation = SupportsAggregation, Aggregating = Aggregating, SyncError = SyncError });
            }

            public Task<AuthenticationProvider> SaveAsync(AuthenticationProviderEdit edit, CancellationToken cancellationToken)
            {
                SaveCalls++;
                if (Failure != null)
                {
                    throw Failure;
                }
                LastEdit = edit;
                var provider = Provider(edit.Id);
                provider.Name = edit.Name;
                Providers.RemoveAll(item => item.Id == edit.Id);
                Providers.Add(provider);
                return Task.FromResult(provider);
            }

            public Task<AuthenticationProvider> CopyAsync(AuthenticationProvider provider, string name, CancellationToken cancellationToken)
            {
                CopyCalls++;
                var copy = Provider("copy");
                copy.Name = name;
                Providers.Add(copy);
                return Task.FromResult(copy);
            }

            public Task<bool> DeleteAsync(AuthenticationProvider provider, CancellationToken cancellationToken)
            {
                DeleteCalls++;
                Providers.RemoveAll(item => item.Id == provider.Id);
                return Task.FromResult(false);
            }

            public Task<AuthenticationApplyPlan> PreflightAsync(AuthenticationProvider provider, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<AuthenticationOperation> ApplyAsync(AuthenticationApplyPlan plan, string operationId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<AuthenticationOperation> GetOperationAsync(string operationId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<AuthenticationAccountsSnapshot> ReadAccountsAsync(CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<AuthenticationDeviceLogin> StartDeviceLoginAsync(string targetAccountId, string loginId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<AuthenticationDeviceLogin> ReadDeviceLoginAsync(string loginId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<AuthenticationDeviceLogin> CancelDeviceLoginAsync(string loginId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            internal AuthenticationSessionHistoryState SessionHistory = new AuthenticationSessionHistoryState();
            internal AuthenticationSessionHistoryPreview Preview;
            internal Exception SessionHistoryFailure;
            internal int SessionHistoryReads;

            public Task<AuthenticationSessionHistoryState> ReadSessionHistoryAsync(CancellationToken cancellationToken)
            {
                SessionHistoryReads++;
                if (SessionHistoryFailure != null) throw SessionHistoryFailure;
                return Task.FromResult(SessionHistory);
            }

            public Task<AuthenticationSessionHistoryPreview> PreviewSessionHistoryAsync(CancellationToken cancellationToken)
            {
                if (Preview == null) throw new NotSupportedException();
                return Task.FromResult(Preview);
            }

            public Task<AuthenticationSessionHistoryResult> SetSessionHistoryAsync(bool unified, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<AuthenticationSessionRepairResult> RepairSessionHistoryAsync(string threadId,
                AuthenticationSessionRepairMode mode, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            internal AuthenticationStackState Stack = new AuthenticationStackState();
            internal readonly List<string> StackCalls = new List<string>();

            public Task<AuthenticationStackState> ReadStackAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(Stack);
            }

            public Task<AuthenticationStackState> EnterStackAsync(string defaultProviderId, IReadOnlyList<string> memberIds, CancellationToken cancellationToken)
            {
                StackCalls.Add("enter:" + defaultProviderId + ":" + string.Join(",", memberIds));
                var members = new List<AuthenticationStackMember> { new AuthenticationStackMember { ProviderId = defaultProviderId, IsDefault = true } };
                members.AddRange(memberIds.Select(id => new AuthenticationStackMember { ProviderId = id, ModelIds = new List<string> { "ccs-" + id + "/test" } }));
                Stack = new AuthenticationStackState { Active = true, DefaultProviderId = defaultProviderId, Members = members, Providers = Stack.Providers, GlobalApplied = true };
                Aggregating = true;
                return Task.FromResult(Stack);
            }

            public Task<AuthenticationStackState> ExitStackAsync(CancellationToken cancellationToken)
            {
                StackCalls.Add("exit");
                var members = Stack.Members.Select(member => new AuthenticationStackMember { ProviderId = member.ProviderId, ModelIds = member.ModelIds }).ToList();
                Stack = new AuthenticationStackState { Members = members, Providers = Stack.Providers, GlobalApplied = true };
                Aggregating = false;
                return Task.FromResult(Stack);
            }

            public Task<AuthenticationStackState> SetStackMemberAsync(string providerId, bool enabled, CancellationToken cancellationToken)
            {
                StackCalls.Add((enabled ? "join:" : "leave:") + providerId);
                var members = Stack.Members.Where(member => member.ProviderId != providerId).ToList();
                if (enabled) members.Add(new AuthenticationStackMember { ProviderId = providerId, ModelIds = new List<string> { "ccs-" + providerId + "/test" } });
                Stack = new AuthenticationStackState { Active = Stack.Active, DefaultProviderId = Stack.DefaultProviderId, Members = members, Providers = Stack.Providers, GlobalApplied = Stack.Active };
                return Task.FromResult(Stack);
            }

            public Task<AuthenticationStackState> SetStackDefaultAsync(string providerId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<AuthenticationStackState> AdoptStackCatalogAsync(CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<AuthenticationStackState> SetStackClassicSubagentsAsync(bool enabled, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class Reader : IScreenReaderService
        {
            internal int SpeakCalls;
            internal string LastSpokenText;
            public bool IsAvailable
            {
                get
                {
                    return true;
                }
            }

            public bool IsInitialized
            {
                get
                {
                    return true;
                }
            }

            public string LastError
            {
                get
                {
                    return null;
                }
            }

            public bool EnsureInitialized()
            {
                return true;
            }

            public bool IsReaderRunning()
            {
                return true;
            }

            public int GetReaderState()
            {
                return 1;
            }

            public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
            {
                SpeakCalls++;
                LastSpokenText = text;
                return true;
            }

            public bool Stop(bool useSlave)
            {
                return true;
            }

            public void Dispose()
            {
            }
        }
    }
}
