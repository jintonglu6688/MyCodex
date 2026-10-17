using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AutomationRunDetailsFormTests
    {
        [Test]
        public void AutomationRunDetailsForm_DisplaysReadonlyConversationControls()
        {
            var run = new AutomationRunSnapshot
            {
                Id = "run-1",
                AutomationId = "automation-1",
                Status = AutomationRunStatus.Failed,
                StartedAt = new DateTime(2026, 6, 6, 8, 0, 0),
                CompletedAt = new DateTime(2026, 6, 6, 8, 5, 0),
                Summary = "Reviewed reports.",
                ErrorMessage = "One report failed.",
                ThreadId = "thread-1",
                WorkspacePath = @"E:\Documents\Reports",
                GeneratedArtifacts = new List<string> { @"E:\Documents\Reports\summary.docx" },
                ChangedFiles = new List<string> { @"src\Report.cs" }
            };

            using (var form = new AutomationRunDetailsForm(run, "Daily reports", new AutomationRunActionCallbacks(), new AutomationRunActivityCallbacks()))
            {
                Assert.That(form.Text, Is.EqualTo(UiLocalization.Text("AutomationRunDetailsForm.Title", "Automation run details")));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(760));
                Assert.That(FindControl<ChatHistoryListBox>(form, UiLocalization.Text("AutomationRunDetailsForm.Accessible.Conversation", "Messages")), Is.Not.Null);
                Assert.That(FindActivityControl(form, UiLocalization.Text("AutomationRunDetailsForm.Accessible.Activity", "Activity")), Is.Not.Null);
                Assert.That(FindControl<Label>(form, UiLocalization.Text("AutomationRunDetailsForm.Accessible.Status", "Status")), Is.Not.Null);
                Assert.That(FindControl<Button>(form, StripMnemonic(UiLocalization.Text("AutomationRunDetailsForm.Button.Refresh", "Refresh(&R)"))), Is.Not.Null);
                Assert.That(FindControl<Button>(form, StripMnemonic(UiLocalization.Text("AutomationRunDetailsForm.Button.OpenThread", "Open in main window"))), Is.Not.Null);
                Assert.That(FindControl<Button>(form, StripMnemonic(UiLocalization.Text("AutomationRunDetailsForm.Button.OpenWorkspace", "Open workspace"))), Is.Not.Null);
                Assert.That(FindControl<Button>(form, StripMnemonic(UiLocalization.Text("AutomationRunDetailsForm.Button.Close", "Close"))), Is.Not.Null);

                Assert.That(FindControl<TextBox>(form, "Summary"), Is.Null);
                Assert.That(FindControl<TextBox>(form, "Error"), Is.Null);
                Assert.That(FindControl<ListView>(form, "Generated artifacts"), Is.Null);
                Assert.That(FindControl<ListView>(form, "Changed files"), Is.Null);
            }
        }

        [Test]
        public void AutomationRunDetailsForm_EscapeClosesForm()
        {
            using (var form = new AutomationRunDetailsForm(new AutomationRunSnapshot(), "Daily reports", new AutomationRunActionCallbacks()))
            {
                form.Show();

                var handled = InvokeProcessCmdKey(form, Keys.Escape);

                Assert.That(handled, Is.True);
                Assert.That(form.IsDisposed, Is.True);
            }
        }

        [Test]
        public void AutomationRunDetailsForm_LoadsConversationIntoChatHistoryList()
        {
            var run = new AutomationRunSnapshot
            {
                Id = "run-1",
                ThreadId = "thread-1"
            };
            var callbacks = new AutomationRunActionCallbacks
            {
                LoadConversationAsync = selectedRun => Task.FromResult<IReadOnlyList<ChatHistoryItem>>(new[]
                {
                    new ChatHistoryItem
                    {
                        Sender = "User",
                        Category = ChatHistoryCategory.Conversation,
                        DisplayText = "User: collect news",
                        FullText = "User: collect news",
                        AccessibleText = "User: collect news"
                    },
                    new ChatHistoryItem
                    {
                        Sender = "Codex",
                        Category = ChatHistoryCategory.Conversation,
                        DisplayText = "Codex: done",
                        FullText = "Codex: done",
                        AccessibleText = "Codex: done"
                    }
                })
            };

            using (var form = new AutomationRunDetailsForm(run, "Daily reports", callbacks))
            {
                form.Show();
                WaitForCondition(() =>
                {
                    var history = FindControl<ChatHistoryListBox>(form, UiLocalization.Text("AutomationRunDetailsForm.Accessible.Conversation", "Messages"));
                    return history != null &&
                           history.Items.OfType<ChatHistoryItem>().Any(item => item.FullText == "Codex: done");
                });

                var history = FindControl<ChatHistoryListBox>(form, UiLocalization.Text("AutomationRunDetailsForm.Accessible.Conversation", "Messages"));
                Assert.That(history.Items.OfType<ChatHistoryItem>().Select(item => item.FullText).ToArray(), Does.Contain("User: collect news"));
                Assert.That(history.Items.OfType<ChatHistoryItem>().Select(item => item.FullText).ToArray(), Does.Contain("Codex: done"));

                history.SelectSingleIndex(1);
                Assert.That(history.BuildClipboardTextForSelection(), Is.EqualTo("Codex: done"));
            }
        }

        [Test]
        public void AutomationRunDetailsForm_RefreshReloadsConversation()
        {
            var run = new AutomationRunSnapshot
            {
                Id = "run-1",
                ThreadId = "thread-1"
            };
            var loadCount = 0;
            var callbacks = new AutomationRunActionCallbacks
            {
                LoadConversationAsync = selectedRun =>
                {
                    loadCount++;
                    return Task.FromResult<IReadOnlyList<ChatHistoryItem>>(new[]
                    {
                        new ChatHistoryItem
                        {
                            Sender = "Codex",
                            Category = ChatHistoryCategory.Conversation,
                            DisplayText = "Codex: load " + loadCount,
                            FullText = "Codex: load " + loadCount,
                            AccessibleText = "Codex: load " + loadCount
                        }
                    });
                }
            };

            using (var form = new AutomationRunDetailsForm(run, "Daily reports", callbacks, new AutomationRunActivityCallbacks()))
            {
                form.Show();
                WaitForConversationItem(form, "Codex: load 1");

                var refreshButton = FindControl<Button>(form, StripMnemonic(UiLocalization.Text("AutomationRunDetailsForm.Button.Refresh", "Refresh(&R)")));
                refreshButton.PerformClick();

                WaitForConversationItem(form, "Codex: load 2");
                Assert.That(loadCount, Is.EqualTo(2));
            }
        }

        [Test]
        public void AutomationRunDetailsForm_LoadsAndUpdatesActivityItems()
        {
            Action<IReadOnlyList<string>> subscriber = null;
            var callbacks = new AutomationRunActivityCallbacks
            {
                GetActivityItems = threadId => new[] { "Tool: first activity" },
                SubscribeActivityUpdates = (threadId, onUpdated) =>
                {
                    subscriber = onUpdated;
                    return new DisposableAction(() => subscriber = null);
                }
            };

            using (var form = new AutomationRunDetailsForm(
                new AutomationRunSnapshot { ThreadId = "thread-1" },
                "Daily reports",
                new AutomationRunActionCallbacks(),
                callbacks))
            {
                form.Show();
                var activity = FindActivityControl(form, UiLocalization.Text("AutomationRunDetailsForm.Accessible.Activity", "Activity"));
                Assert.That(activity.AccessibilityObject.Value, Does.Contain("Tool: first activity"));

                subscriber(new[] { "Tool: second activity" });
                System.Windows.Forms.Application.DoEvents();

                Assert.That(activity.AccessibilityObject.Value, Does.Contain("Tool: second activity"));
            }
        }

        [Test]
        public void AutomationRunDetailsForm_ClosesActivitySubscriptionWhenDisposed()
        {
            var disposed = false;
            var callbacks = new AutomationRunActivityCallbacks
            {
                GetActivityItems = threadId => new string[0],
                SubscribeActivityUpdates = (threadId, onUpdated) => new DisposableAction(() => disposed = true)
            };

            var form = new AutomationRunDetailsForm(
                new AutomationRunSnapshot { ThreadId = "thread-1" },
                "Daily reports",
                new AutomationRunActionCallbacks(),
                callbacks);

            form.Show();
            form.Close();
            form.Dispose();

            Assert.That(disposed, Is.True);
        }

        private static T FindControl<T>(Control root, string accessibleName) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var typed = child as T;
                if (typed != null && child.AccessibleName == accessibleName)
                {
                    return typed;
                }

                var nested = FindControl<T>(child, accessibleName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static Control FindActivityControl(Control root, string accessibleName)
        {
            foreach (Control child in root.Controls)
            {
                if (child.GetType().Name == "ActivityVirtualBrowseControl" &&
                    child.AccessibleName == accessibleName)
                {
                    return child;
                }

                var nested = FindActivityControl(child, accessibleName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static void WaitForConversationItem(Form form, string fullText)
        {
            WaitForCondition(() =>
            {
                var history = FindControl<ChatHistoryListBox>(
                    form,
                    UiLocalization.Text("AutomationRunDetailsForm.Accessible.Conversation", "Messages"));
                return history != null &&
                       history.Items.OfType<ChatHistoryItem>().Any(item => item.FullText == fullText);
            });
        }

        private static string StripMnemonic(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace("&", string.Empty).Replace("...", string.Empty).Trim();
        }

        private static bool InvokeProcessCmdKey(Form form, Keys keyData)
        {
            var message = new Message();
            return (bool)typeof(Form)
                .GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(form, new object[] { message, keyData });
        }

        private static void WaitForCondition(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                System.Windows.Forms.Application.DoEvents();
                if (condition())
                {
                    return;
                }

                Thread.Sleep(10);
            }

            Assert.Fail("Condition was not satisfied before the timeout.");
        }
    }
}
