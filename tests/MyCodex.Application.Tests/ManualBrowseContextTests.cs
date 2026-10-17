using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ManualBrowseContextTests
    {
        [Test]
        public void Contexts_KeepTargetAndCursorsIndependent()
        {
            var firstSpeech = new List<string>();
            var secondSpeech = new List<string>();
            var firstVisibleIndexes = new List<int>();
            var secondVisibleIndexes = new List<int>();
            var first = CreateContext(
                new[] { CreateItem("first context message", "first context line") },
                new[] { "first terminal" },
                new[] { "first activity" },
                firstSpeech,
                firstVisibleIndexes);
            var second = CreateContext(
                new[] { CreateItem("second context message", "second context line") },
                new[] { "second terminal" },
                new[] { "second activity" },
                secondSpeech,
                secondVisibleIndexes);

            Assert.That(first.HandleHotkey(Keys.Alt | Keys.Down), Is.True);
            Assert.That(first.HandleHotkey(Keys.Alt | Keys.Right), Is.True);
            Assert.That(first.HandleHotkey(Keys.Alt | Keys.Delete), Is.True);
            Assert.That(first.HandleHotkey(Keys.Alt | Keys.Down), Is.True);

            Assert.That(second.Target, Is.EqualTo(ManualReadTarget.Message));
            Assert.That(second.HandleHotkey(Keys.Alt | Keys.Down), Is.True);

            Assert.That(first.Target, Is.EqualTo(ManualReadTarget.Terminal));
            Assert.That(firstSpeech.Last(), Is.EqualTo("first terminal"));
            Assert.That(secondSpeech.Last(), Does.Contain("second context message"));
            Assert.That(firstVisibleIndexes, Is.EqualTo(new[] { 0 }));
            Assert.That(secondVisibleIndexes, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void AltEnter_OpensTheVirtualMessageInsteadOfTheNativeSelection()
        {
            ChatHistoryItem opened = null;
            var messages = new[]
            {
                CreateItem("first message"),
                CreateItem("second message")
            };
            var context = CreateContext(messages, new string[0], new string[0], new List<string>(), new List<int>(), item => opened = item);

            context.HandleHotkey(Keys.Alt | Keys.Down);
            context.HandleHotkey(Keys.Alt | Keys.Down);

            Assert.That(context.HandleHotkey(Keys.Alt | Keys.Enter), Is.True);
            Assert.That(opened, Is.SameAs(messages[1]));
        }

        [Test]
        public void SourceReplacement_DoesNotMoveVirtualCursorToAnUnrelatedMessage()
        {
            var messages = new List<ChatHistoryItem>
            {
                CreateItem("first message"),
                CreateItem("second message"),
                CreateItem("third message")
            };
            var speech = new List<string>();
            var visibleIndexes = new List<int>();
            var context = CreateContext(
                () => messages.ToArray(),
                new string[0],
                new string[0],
                speech,
                visibleIndexes);

            context.HandleHotkey(Keys.Alt | Keys.End);
            messages.RemoveRange(1, 2);

            Assert.That(context.HandleHotkey(Keys.Alt | Keys.Down), Is.True);
            Assert.That(speech.Last(), Does.Not.Contain("first message"));
            Assert.That(visibleIndexes, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void MessagesInTheSameTurn_AltUpMovesOnlyToTheImmediatelyPreviousMessage()
        {
            var messages = new[]
            {
                CreateConversationItem("User", "older request", "thread-1", "turn-1", 1),
                CreateConversationItem("Codex", "older reply", "thread-1", "turn-1", 1),
                CreateConversationItem("User", "current request", "thread-1", "turn-2", 2),
                CreateConversationItem("Codex", "current reply", "thread-1", "turn-2", 2)
            };
            var speech = new List<string>();
            var visibleIndexes = new List<int>();
            var context = CreateContext(
                messages,
                new string[0],
                new string[0],
                speech,
                visibleIndexes);

            context.HandleHotkey(Keys.Alt | Keys.End);
            context.HandleHotkey(Keys.Alt | Keys.Up);

            Assert.That(speech.Last(), Does.Contain("current request"));
            Assert.That(visibleIndexes.Last(), Is.EqualTo(2));

            context.HandleHotkey(Keys.Alt | Keys.Down);

            Assert.That(speech.Last(), Does.Contain("current reply"));
            Assert.That(visibleIndexes.Last(), Is.EqualTo(3));
        }

        [Test]
        public void RepeatedLocalMessageText_AltUpDoesNotResolveToAnEarlierOccurrence()
        {
            var messages = new[]
            {
                CreateConversationItem("User", "repeat request", "thread-1", "turn-1", 1),
                CreateConversationItem("Codex", "older reply", "thread-1", "turn-1", 1),
                CreateConversationItem("Me", "repeat request", "thread-1", string.Empty, 0)
            };
            var speech = new List<string>();
            var visibleIndexes = new List<int>();
            var context = CreateContext(
                messages,
                new string[0],
                new string[0],
                speech,
                visibleIndexes);

            context.HandleHotkey(Keys.Alt | Keys.End);
            context.HandleHotkey(Keys.Alt | Keys.Up);

            Assert.That(speech.Last(), Does.Contain("older reply"));
            Assert.That(visibleIndexes.Last(), Is.EqualTo(1));
        }

        [Test]
        public void SourceReplacement_RestoresVirtualMessageByIdentityAfterSystemHeaderIsRemoved()
        {
            ChatHistoryItem opened = null;
            var firstItem = CreateItem("first message");
            firstItem.ThreadId = "thread-1";
            firstItem.TurnId = "turn-1";
            firstItem.TurnIndex = 1;
            var selectedItem = CreateItem("selected message");
            selectedItem.ThreadId = "thread-1";
            selectedItem.TurnId = "turn-2";
            selectedItem.TurnIndex = 2;
            var messages = new List<ChatHistoryItem>
            {
                ChatHistoryItemFactory.Create("System", "session header", ChatHistoryCategory.System),
                firstItem,
                selectedItem
            };
            var context = CreateContext(
                () => messages.ToArray(),
                new string[0],
                new string[0],
                new List<string>(),
                new List<int>(),
                item => opened = item);

            context.HandleHotkey(Keys.Alt | Keys.End);

            messages.Clear();
            messages.Add(ChatHistoryItemFactory.Create("System", "Loading history", ChatHistoryCategory.System));
            messages.Clear();
            var reloadedFirstItem = CreateItem("first message");
            reloadedFirstItem.ThreadId = "thread-1";
            reloadedFirstItem.TurnId = "turn-1";
            reloadedFirstItem.TurnIndex = 1;
            var reloadedSelectedItem = CreateItem("selected message");
            reloadedSelectedItem.ThreadId = "thread-1";
            reloadedSelectedItem.TurnId = "turn-2";
            reloadedSelectedItem.TurnIndex = 2;
            messages.Add(reloadedFirstItem);
            messages.Add(reloadedSelectedItem);

            Assert.That(context.HandleHotkey(Keys.Alt | Keys.Enter), Is.True);
            Assert.That(opened, Is.SameAs(reloadedSelectedItem));
        }

        [Test]
        public void SourceReplacement_KeepsVirtualMessageLineCursorForTheSameMessage()
        {
            var originalItem = CreateItem("first line\nsecond line\nthird line");
            originalItem.ThreadId = "thread-1";
            originalItem.TurnId = "turn-1";
            originalItem.TurnIndex = 1;
            var messages = new List<ChatHistoryItem> { originalItem };
            var speech = new List<string>();
            var context = CreateContext(
                () => messages.ToArray(),
                new string[0],
                new string[0],
                speech,
                new List<int>());

            context.HandleHotkey(Keys.Alt | Keys.Down);
            context.HandleHotkey(Keys.Alt | Keys.Right);
            context.HandleHotkey(Keys.Alt | Keys.Right);

            var reloadedItem = CreateItem("first line\nsecond line\nthird line");
            reloadedItem.ThreadId = "thread-1";
            reloadedItem.TurnId = "turn-1";
            reloadedItem.TurnIndex = 1;
            messages[0] = reloadedItem;

            Assert.That(context.HandleHotkey(Keys.Alt | Keys.Right), Is.True);
            Assert.That(speech.Last(), Is.EqualTo("third line"));
        }

        [Test]
        public void TerminalHotkey_ReadsTerminalWithoutChangingAnotherTarget()
        {
            var firstSpeech = new List<string>();
            var second = CreateContext(
                new[] { CreateItem("second") },
                new[] { "second terminal" },
                new string[0],
                new List<string>(),
                new List<int>());
            var first = CreateContext(
                new[] { CreateItem("first") },
                new[] { "first terminal" },
                new string[0],
                firstSpeech,
                new List<int>());

            Assert.That(first.HandleTerminalHotkey(Keys.Alt | Keys.Down), Is.True);

            Assert.That(first.Target, Is.EqualTo(ManualReadTarget.Terminal));
            Assert.That(firstSpeech.Last(), Is.EqualTo("first terminal"));
            Assert.That(second.Target, Is.EqualTo(ManualReadTarget.Message));
        }

        [Test]
        public void NonBrowseTerminalHotkey_DoesNotChangeTheCurrentTarget()
        {
            var context = CreateContext(
                new[] { CreateItem("message") },
                new[] { "terminal" },
                new string[0],
                new List<string>(),
                new List<int>());

            Assert.That(context.HandleTerminalHotkey(Keys.Alt | Keys.A), Is.False);
            Assert.That(context.Target, Is.EqualTo(ManualReadTarget.Message));
        }

        [Test]
        public void ResetTerminalCursor_StartsReadingNewTerminalContentFromItsFirstLine()
        {
            var terminalLines = new List<string> { "first", "second" };
            var speech = new List<string>();
            var context = new ManualBrowseContext(new ManualBrowseContextCallbacks
            {
                GetMessages = () => new ChatHistoryItem[0],
                GetTerminalLines = () => terminalLines.ToArray(),
                GetActivityItems = () => new string[0],
                PrepareManualReadOutput = () => { },
                SpeakManualText = text => speech.Add(text),
                SetStatus = text => speech.Add(text)
            });

            context.HandleTerminalHotkey(Keys.Alt | Keys.Down);
            context.HandleTerminalHotkey(Keys.Alt | Keys.Down);
            terminalLines.Clear();
            terminalLines.Add("new first");

            context.ResetTerminalCursor();
            context.HandleTerminalHotkey(Keys.Alt | Keys.Down);

            Assert.That(speech.Last(), Is.EqualTo("new first"));
        }

        private static ManualBrowseContext CreateContext(
            ChatHistoryItem[] messages,
            string[] terminalLines,
            string[] activityItems,
            ICollection<string> speech,
            ICollection<int> visibleIndexes,
            Action<ChatHistoryItem> openMessageDetail = null)
        {
            return CreateContext(
                () => messages,
                terminalLines,
                activityItems,
                speech,
                visibleIndexes,
                openMessageDetail);
        }

        private static ManualBrowseContext CreateContext(
            Func<ChatHistoryItem[]> messages,
            string[] terminalLines,
            string[] activityItems,
            ICollection<string> speech,
            ICollection<int> visibleIndexes,
            Action<ChatHistoryItem> openMessageDetail = null)
        {
            return new ManualBrowseContext(new ManualBrowseContextCallbacks
            {
                GetMessages = messages,
                EnsureMessageVisible = index => visibleIndexes.Add(index),
                GetTerminalLines = () => terminalLines,
                GetActivityItems = () => activityItems,
                OpenMessageDetail = openMessageDetail,
                PrepareManualReadOutput = () => { },
                SpeakManualText = text => speech.Add(text),
                SetStatus = text => speech.Add(text),
                AllowLongMessageSpeech = () => true
            });
        }

        private static ChatHistoryItem CreateItem(string firstLine, string secondLine = null)
        {
            return new ChatHistoryItem
            {
                Sender = "user",
                DisplayText = firstLine,
                FullText = string.IsNullOrWhiteSpace(secondLine)
                    ? firstLine
                    : firstLine + Environment.NewLine + secondLine
            };
        }

        private static ChatHistoryItem CreateConversationItem(
            string sender,
            string message,
            string threadId,
            string turnId,
            int turnIndex)
        {
            var item = ChatHistoryItemFactory.Create(sender, message, ChatHistoryCategory.Conversation);
            item.ThreadId = threadId;
            item.TurnId = turnId;
            item.TurnIndex = turnIndex;
            return item;
        }
    }
}
