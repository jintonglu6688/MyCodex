using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class SessionWorkbenchFormTests
    {
        [Test]
        public void TargetExitSavesSelectionAndDraftBeforeCommitAndDoesNotSaveAgainOnClose()
        {
            var selections = 0;
            var drafts = 0;
            using (var form = CreateForm(saveMessageSelection: snapshot => selections++, saveDraft: draft => drafts++))
            {
                Show(form);
                form.SaveStateForTargetExit();
                Assert.That(selections, Is.EqualTo(1));
                Assert.That(drafts, Is.EqualTo(1));
                form.CommitTargetExit();
                form.Close();
                Assert.That(selections, Is.EqualTo(1));
                Assert.That(drafts, Is.EqualTo(1));
            }
        }

        [Test]
        public void TabFromLastToolButtonMovesToMessages()
        {
            using (UseCulture("en-US"))
            using (var form = CreateForm())
            {
                Show(form);
                FindButton(form, "Subagents").Focus();
                WaitForCondition(() => FindButton(form, "Subagents").Focused);

                Assert.That(InvokeProcessCmdKey(form, Keys.Tab), Is.True);
                WaitForCondition(() => FindSingleControl<ChatHistoryListBox>(form).Focused);
            }
        }

        [Test]
        public void ShiftTabFromFirstToolButtonMovesToActivity()
        {
            using (UseCulture("en-US"))
            using (var form = CreateForm())
            {
                Show(form);
                FindButton(form, "File browser").Focus();
                WaitForCondition(() => FindButton(form, "File browser").Focused);

                Assert.That(InvokeProcessCmdKey(form, Keys.Shift | Keys.Tab), Is.True);
                WaitForCondition(() => FindSingleControl<ActivityVirtualBrowseControl>(form).Focused);
            }
        }

        [Test]
        public void ShiftTabFromActivityMovesToLastChatControl()
        {
            using (UseCulture("en-US"))
            using (var form = CreateForm())
            {
                Show(form);
                FindSingleControl<ActivityVirtualBrowseControl>(form).Focus();
                WaitForCondition(() => FindSingleControl<ActivityVirtualBrowseControl>(form).Focused);

                Assert.That(InvokeProcessCmdKey(form, Keys.Shift | Keys.Tab), Is.True);
                WaitForCondition(() => FindSingleControl<AttachmentComposerTextBox>(form).Focused);
            }
        }

        [Test]
        public void AltMFocusesMessagesLikeMainForm()
        {
            using (UseCulture("en-US"))
            using (var form = CreateForm())
            {
                Show(form);
                FindButton(form, "File browser").Focus();
                WaitForCondition(() => FindButton(form, "File browser").Focused);

                Assert.That(InvokeProcessCmdKey(form, Keys.Alt | Keys.M), Is.True);
                WaitForCondition(() => FindSingleControl<ChatHistoryListBox>(form).Focused);
            }
        }

        [Test]
        public void AltIFocusesInputLikeMainForm()
        {
            using (UseCulture("en-US"))
            using (var form = CreateForm())
            {
                Show(form);
                FindButton(form, "File browser").Focus();
                WaitForCondition(() => FindButton(form, "File browser").Focused);

                Assert.That(InvokeProcessCmdKey(form, Keys.Alt | Keys.I), Is.True);
                WaitForCondition(() => FindSingleControl<AttachmentComposerTextBox>(form).Focused);
            }
        }

        [Test]
        public void AltLFocusesActivityLikeMainForm()
        {
            using (UseCulture("en-US"))
            using (var form = CreateForm())
            {
                Show(form);
                FindButton(form, "File browser").Focus();
                WaitForCondition(() => FindButton(form, "File browser").Focused);

                Assert.That(InvokeProcessCmdKey(form, Keys.Alt | Keys.L), Is.True);
                WaitForCondition(() => FindSingleControl<ActivityVirtualBrowseControl>(form).Focused);
            }
        }

        [Test]
        public void ManualBrowseHotkeysAreHandledBeforeEmbeddedChatKeys()
        {
            var speech = new List<string>();
            using (var form = CreateForm(manualBrowseContext: CreateManualBrowseContext(speech)))
            {
                Assert.That(InvokeProcessCmdKey(form, Keys.Alt | Keys.Down), Is.True);
                Assert.That(speech.Last(), Does.Contain("virtual message"));
            }
        }

        [Test]
        public void ManualBrowseAltEnterDoesNotFallBackToTheNativeListOutsideTheMessageTarget()
        {
            var speech = new List<string>();
            var context = CreateManualBrowseContext(speech);
            context.HandleHotkey(Keys.Alt | Keys.Delete);
            using (var form = CreateForm(manualBrowseContext: context))
            {
                Assert.That(InvokeProcessCmdKey(form, Keys.Alt | Keys.Enter), Is.True);
            }
        }

        [Test]
        public void InputAndActivityTitlesExposeTheMainFormMnemonics()
        {
            using (UseCulture("en-US"))
            using (var form = CreateForm())
            {
                var labels = form.Controls.Cast<Control>().SelectMany(Flatten).OfType<Label>().ToArray();

                Assert.That(
                    labels.Any(label => string.Equals(label.AccessibleName, "Input", StringComparison.Ordinal) &&
                                        string.Equals(label.Text, "Input(&I)", StringComparison.Ordinal)),
                    Is.True);
                Assert.That(
                    labels.Any(label => string.Equals(label.AccessibleName, "Activity", StringComparison.Ordinal) &&
                                        string.Equals(label.Text, "Activity(&L)", StringComparison.Ordinal)),
                    Is.True);
            }
        }

        [Test]
        public void TurnEventsUpdateTheWindowTitleWithTheWorkingStatus()
        {
            using (UseCulture("en-US"))
            using (var form = CreateForm())
            {
                var idleTitle = form.Text;

                form.HandleAppServerEvent(new AppServerEventArgs(
                    "turn_started",
                    "started",
                    threadId: "thread-1",
                    turnId: "turn-1"));

                Assert.That(form.Text, Is.EqualTo("Working - " + idleTitle));

                form.HandleAppServerEvent(new AppServerEventArgs(
                    "turn_completed",
                    "completed",
                    threadId: "thread-1",
                    turnId: "turn-1"));

                Assert.That(form.Text, Is.EqualTo(idleTitle));
            }
        }

        [Test]
        public void TurnTerminalClearsTheWindowTitleBeforeTheHostBusyStateIsCleared()
        {
            using (UseCulture("en-US"))
            using (var form = CreateForm(isWorking: session => true))
            {
                Assert.That(form.Text, Does.StartWith("Working - "));
                var idleTitle = form.Text.Substring("Working - ".Length);

                form.HandleAppServerEvent(new AppServerEventArgs(
                    "turn_completed",
                    "completed",
                    threadId: "thread-1",
                    turnId: "turn-1"));

                Assert.That(form.Text, Is.EqualTo(idleTitle));
            }
        }

        [Test]
        public void TurnTerminalReloadsDetachedHistoryAfterAnEventInvalidatesIt()
        {
            using (UseCulture("en-US"))
            {
                var readCount = 0;
                var workflow = new SessionHistoryLoadWorkflow(
                    new SessionHistoryStateStore(),
                    new SessionHistoryDisplayBuilder(
                        "System",
                        "Error",
                        session => "model",
                        CreateHistoryItem,
                        ResolveHistoryCategory),
                    threadId =>
                    {
                        readCount++;
                        return Task.FromResult(new AppServerThreadDetails
                        {
                            ThreadId = threadId,
                            TurnCount = 1,
                            Messages =
                            {
                                new AppServerThreadMessage
                                {
                                    ThreadId = threadId,
                                    TurnId = "turn-1",
                                    Sender = "codex",
                                    Content = readCount == 1 ? "before completion" : "after completion"
                                }
                            }
                        });
                    },
                    _ => false,
                    _ => false);
                var cachedRequest = workflow.BuildRequest(new ChatSession
                {
                    Id = "session-1",
                    ThreadId = "thread-1",
                    Title = "Detached workbench"
                }, false);
                workflow.CompleteRemoteLoadAsync(cachedRequest, (session, threadId) => true, null)
                    .GetAwaiter()
                    .GetResult();

                using (var form = CreateForm(historyLoadWorkflow: workflow))
                {
                    form.HandleAppServerEvent(new AppServerEventArgs(
                        "turn_completed",
                        "completed",
                        threadId: "thread-1",
                        turnId: "turn-1"));

                    Assert.That(readCount, Is.EqualTo(2));
                    Assert.That(
                        FindSingleControl<ChatHistoryListBox>(form).Items
                            .OfType<ChatHistoryItem>()
                            .Select(item => item.MarkdownText),
                        Does.Contain("after completion"));
                }
            }
        }

        [Test]
        public void LoadingRemoteHistory_DoesNotReplaceExistingMessagesOrTheirFocus()
        {
            using (var form = CreateForm())
            {
                var chatWorkbench = FindSingleControl<ChatWorkbenchControl>(form);
                var existingItem = CreateHistoryItem("codex", "existing reply", ChatHistoryCategory.Conversation);
                existingItem.ThreadId = "thread-1";
                existingItem.TurnId = "turn-1";
                existingItem.TurnIndex = 1;
                chatWorkbench.ReplaceHistoryItems(new[] { existingItem });
                var historyListBox = FindSingleControl<ChatHistoryListBox>(form);
                historyListBox.SelectSingleIndex(0);

                InvokeApplyHistoryLoadResult(
                    form,
                    new SessionHistoryLoadResult(
                        SessionHistoryLoadResultKind.LoadingRemoteHistory,
                        new[] { ChatHistoryItemFactory.Create("System", "Loading history", ChatHistoryCategory.System) },
                        "Loading session history...",
                        SessionHistoryLoadStatusPriority.Normal));

                Assert.That(historyListBox.PrimaryChatHistoryItem, Is.SameAs(existingItem));
                Assert.That(historyListBox.GetSelectedChatHistoryItemsInDisplayOrder(), Is.EqualTo(new[] { existingItem }));
            }
        }

        [Test]
        public void LoadedRemoteHistory_PreservesLocalUserMessageUntilTheRemoteHistoryCatchesUp()
        {
            using (var form = CreateForm())
            {
                var chatWorkbench = FindSingleControl<ChatWorkbenchControl>(form);
                var existingItem = CreateHistoryItem("codex", "existing reply", ChatHistoryCategory.Conversation);
                chatWorkbench.ReplaceHistoryItems(new[] { existingItem });
                var pendingUserItem = CreateHistoryItem("Me", "new question", ChatHistoryCategory.Conversation);
                chatWorkbench.AddHistoryItem(pendingUserItem);

                InvokeApplyHistoryLoadResult(
                    form,
                    new SessionHistoryLoadResult(
                        SessionHistoryLoadResultKind.LoadedRemoteHistory,
                        new[] { CreateHistoryItem("codex", "existing reply", ChatHistoryCategory.Conversation) },
                        null,
                        SessionHistoryLoadStatusPriority.None));

                Assert.That(
                    chatWorkbench.GetHistoryItems().Any(item =>
                        ChatHistoryItemFactory.IsUserSender(item.Sender) &&
                        item.FullText.Contains("new question")),
                    Is.True);
            }
        }

        [Test]
        public void ToolButtonsUseTheMainFormMnemonics()
        {
            using (UseCulture("en-US"))
            using (var form = CreateForm())
            {
                AssertMnemonic(form, "File browser", "MainForm.Workspace.AppServerFiles", "File Browser(&F)...");
                AssertMnemonic(form, "Git workbench", "MainForm.Button.DiffWorkbench", "Git Workbench(&H)");
                AssertMnemonic(form, "Terminal", "MainForm.Button.Command", "Terminal(&K)");
                AssertMnemonic(form, "Subagents", "MainForm.Session.SubAgents", "View Suba&gents...");
                AssertMnemonic(form, "Insert", "MainForm.Button.Attach", "&Attach");
                AssertMnemonic(form, "Send", "MainForm.Button.Send", "&Send");
                AssertMnemonic(form, "Stop", "MainForm.Button.Stop", "&Stop");
                Assert.That(
                    UiLocalization.Text("MainForm.Button.DiffWorkbench", "Git Workbench(&H)"),
                    Is.EqualTo("Git Workbench(&H)"));
            }
        }

        [Test]
        public async Task SuccessfulSend_PlaysTheMainWindowMessageSentSound()
        {
            var playCount = 0;
            using (var form = CreateForm(
                sendAsync: (session, request) => Task.FromResult(new ChatWorkbenchSendResult(true, true, "sent")),
                playMessageSentSound: () => playCount++))
            {
                var chatWorkbench = FindSingleControl<ChatWorkbenchControl>(form);
                chatWorkbench.SetInputText("hello");

                await chatWorkbench.SendCurrentInputAsync();
            }

            Assert.That(playCount, Is.EqualTo(1));
        }

        [Test]
        public async Task FailedSend_DoesNotPlayTheMainWindowMessageSentSound()
        {
            var playCount = 0;
            using (var form = CreateForm(
                sendAsync: (session, request) => Task.FromResult(new ChatWorkbenchSendResult(false, false, "failed")),
                playMessageSentSound: () => playCount++))
            {
                var chatWorkbench = FindSingleControl<ChatWorkbenchControl>(form);
                chatWorkbench.SetInputText("hello");

                await chatWorkbench.SendCurrentInputAsync();
            }

            Assert.That(playCount, Is.Zero);
        }

        [Test]
        public void Escape_DoesNotCloseTheWindowWhenTheSettingIsDisabled()
        {
            using (var form = CreateForm(canCloseWithEscape: () => false))
            {
                var wasClosed = false;
                form.FormClosed += (sender, e) => wasClosed = true;
                Show(form);

                Assert.That(InvokeProcessCmdKey(form, Keys.Escape), Is.True);
                Assert.That(wasClosed, Is.False);
                Assert.That(form.IsDisposed, Is.False);
            }
        }

        [Test]
        public void CtrlW_ClosesTheWindowWhenEscapeClosingIsDisabled()
        {
            var form = CreateForm(canCloseWithEscape: () => false);
            try
            {
                var wasClosed = false;
                form.FormClosed += (sender, e) => wasClosed = true;
                Show(form);

                Assert.That(InvokeProcessCmdKey(form, Keys.Control | Keys.W), Is.True);
                Assert.That(wasClosed, Is.True);
            }
            finally
            {
                if (!form.IsDisposed)
                {
                    form.Dispose();
                }
            }
        }

        [Test]
        public void ClosingTheWindow_CapturesTheFocusedMessageForTheNextOpen()
        {
            ChatHistorySelectionSnapshot capturedSnapshot = null;
            var historyItem = CreateHistoryItem("codex", "selected reply", ChatHistoryCategory.Conversation);
            historyItem.ThreadId = "thread-1";
            historyItem.TurnId = "turn-1";
            historyItem.TurnIndex = 1;
            var form = CreateForm(saveMessageSelection: snapshot => capturedSnapshot = snapshot);
            try
            {
                Show(form);
                var chatWorkbench = FindSingleControl<ChatWorkbenchControl>(form);
                chatWorkbench.ReplaceHistoryItems(new[] { historyItem });
                FindSingleControl<ChatHistoryListBox>(form).SelectSingleIndex(0);

                form.Close();

                Assert.That(capturedSnapshot, Is.Not.Null);
                Assert.That(capturedSnapshot.ResolveMatchingIndex(new[] { historyItem }), Is.EqualTo(0));
            }
            finally
            {
                if (!form.IsDisposed)
                {
                    form.Dispose();
                }
            }
        }

        [Test]
        public void LoadedHistory_RestoresThePreviousMessageListFocus()
        {
            var selectedItem = CreateHistoryItem("codex", "selected reply", ChatHistoryCategory.Conversation);
            selectedItem.ThreadId = "thread-1";
            selectedItem.TurnId = "turn-1";
            selectedItem.TurnIndex = 1;
            var snapshot = ChatHistorySelectionSnapshot.Capture(selectedItem, 0);
            using (var form = CreateForm(loadMessageSelection: () => snapshot))
            {
                Show(form);
                var reloadedItem = CreateHistoryItem("codex", "selected reply", ChatHistoryCategory.Conversation);
                reloadedItem.ThreadId = "thread-1";
                reloadedItem.TurnId = "turn-1";
                reloadedItem.TurnIndex = 1;

                InvokeApplyHistoryLoadResult(
                    form,
                    new SessionHistoryLoadResult(
                        SessionHistoryLoadResultKind.LoadedRemoteHistory,
                        new[] { reloadedItem },
                        null,
                        SessionHistoryLoadStatusPriority.None));

                Assert.That(
                    FindSingleControl<ChatHistoryListBox>(form).PrimaryChatHistoryItem,
                    Is.SameAs(reloadedItem));
            }
        }

        private static SessionWorkbenchForm CreateForm(
            SessionHistoryLoadWorkflow historyLoadWorkflow = null,
            Func<ChatSession, bool> isWorking = null,
            ManualBrowseContext manualBrowseContext = null,
            Func<ChatSession, ChatWorkbenchSendRequest, Task<ChatWorkbenchSendResult>> sendAsync = null,
            Action playMessageSentSound = null,
            Func<bool> canCloseWithEscape = null,
            Action<ChatHistorySelectionSnapshot> saveMessageSelection = null,
            Func<ChatHistorySelectionSnapshot> loadMessageSelection = null,
            Action<ComposeDraftState> saveDraft = null)
        {
            var session = new ChatSession
            {
                Id = "session-1",
                ThreadId = "thread-1",
                Title = "Detached workbench"
            };
            var workspace = new WorkspaceInfo
            {
                Id = "workspace-1",
                Name = "Workspace",
                RootPath = @"E:\\Repo"
            };
            var operations = new SessionWorkbenchOperations
            {
                HistoryLoadWorkflow = historyLoadWorkflow,
                SendAsync = sendAsync ?? ((targetSession, request) => Task.FromResult(new ChatWorkbenchSendResult(true, true, "sent"))),
                StopAsync = targetSession => Task.CompletedTask,
                IsWorking = isWorking ?? (targetSession => false),
                ManualBrowseContext = manualBrowseContext,
                OpenFileBrowser = () => { },
                OpenGitWorkbench = () => { },
                OpenTerminal = () => { },
                OpenSubAgents = () => { },
                CanOpenSubAgents = () => true,
                PlayMessageSentSound = playMessageSentSound,
                CanCloseWithEscape = canCloseWithEscape,
                SaveMessageSelection = saveMessageSelection,
                SaveDraft = saveDraft,
                LoadMessageSelection = loadMessageSelection
            };
            return new SessionWorkbenchForm(
                new SessionWorkbenchContext(session, workspace, null, "draft-scope"),
                operations);
        }

        private static ManualBrowseContext CreateManualBrowseContext(ICollection<string> speech)
        {
            return new ManualBrowseContext(new ManualBrowseContextCallbacks
            {
                GetMessages = () => new[] { CreateHistoryItem("codex", "virtual message", ChatHistoryCategory.Conversation) },
                GetTerminalLines = () => new string[0],
                GetActivityItems = () => new string[0],
                PrepareManualReadOutput = () => { },
                SpeakManualText = text => speech.Add(text),
                SetStatus = text => speech.Add(text)
            });
        }

        private static ChatHistoryItem CreateHistoryItem(string sender, string content, ChatHistoryCategory category)
        {
            return new ChatHistoryItem
            {
                Sender = sender,
                Category = category,
                DisplayText = sender + ": " + content,
                FullText = sender + ": " + content,
                MarkdownText = content
            };
        }

        private static ChatHistoryCategory ResolveHistoryCategory(string sender)
        {
            return string.Equals(sender, "codex", StringComparison.OrdinalIgnoreCase)
                ? ChatHistoryCategory.Conversation
                : ChatHistoryCategory.System;
        }

        private static void Show(Form form)
        {
            form.Show();
            System.Windows.Forms.Application.DoEvents();
        }

        private static void AssertMnemonic(Form form, string accessibleName, string resourceKey, string fallback)
        {
            Assert.That(
                GetMnemonic(FindButton(form, accessibleName).Text),
                Is.EqualTo(GetMnemonic(UiLocalization.Text(resourceKey, fallback))),
                accessibleName);
        }

        private static char GetMnemonic(string text)
        {
            for (var index = 0; index < (text ?? string.Empty).Length - 1; index++)
            {
                if (text[index] != '&')
                {
                    continue;
                }

                if (text[index + 1] == '&')
                {
                    index++;
                    continue;
                }

                return char.ToUpperInvariant(text[index + 1]);
            }

            return char.MinValue;
        }

        private static Button FindButton(Control root, string accessibleName)
        {
            return root.Controls.Cast<Control>()
                .SelectMany(Flatten)
                .OfType<Button>()
                .Single(button => string.Equals(button.AccessibleName, accessibleName, StringComparison.Ordinal));
        }

        private static T FindSingleControl<T>(Control root)
            where T : Control
        {
            return root.Controls.Cast<Control>()
                .SelectMany(Flatten)
                .OfType<T>()
                .Single();
        }

        private static System.Collections.Generic.IEnumerable<Control> Flatten(Control control)
        {
            yield return control;
            foreach (Control child in control.Controls)
            {
                foreach (var nested in Flatten(child))
                {
                    yield return nested;
                }
            }
        }

        private static bool InvokeProcessCmdKey(SessionWorkbenchForm form, Keys keyData)
        {
            var message = new Message();
            var parameters = new object[] { message, keyData };
            return (bool)typeof(SessionWorkbenchForm)
                .GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, parameters);
        }

        private static void InvokeApplyHistoryLoadResult(SessionWorkbenchForm form, SessionHistoryLoadResult result)
        {
            var method = typeof(SessionWorkbenchForm).GetMethod(
                "ApplyHistoryLoadResult",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(form, new object[] { result });
        }

        private static void WaitForCondition(Func<bool> predicate)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                System.Windows.Forms.Application.DoEvents();
                if (predicate())
                {
                    return;
                }

                Thread.Sleep(10);
            }

            Assert.Fail("Condition was not met.");
        }

        private static IDisposable UseCulture(string cultureName)
        {
            return new CultureScope(cultureName);
        }

        private sealed class CultureScope : IDisposable
        {
            private readonly CultureInfo _originalCulture;
            private readonly CultureInfo _originalUiCulture;

            internal CultureScope(string cultureName)
            {
                _originalCulture = CultureInfo.CurrentCulture;
                _originalUiCulture = CultureInfo.CurrentUICulture;
                var culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }

            public void Dispose()
            {
                CultureInfo.CurrentCulture = _originalCulture;
                CultureInfo.CurrentUICulture = _originalUiCulture;
            }
        }
    }
}
