using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ChatHistoryItemFactoryTests
    {
        [TestCase("Me", ChatHistoryCategory.Conversation)]
        [TestCase("User", ChatHistoryCategory.Conversation)]
        [TestCase("codex", ChatHistoryCategory.Conversation)]
        [TestCase("Codex", ChatHistoryCategory.Conversation)]
        [TestCase("Review", ChatHistoryCategory.Conversation)]
        [TestCase("Error", ChatHistoryCategory.Error)]
        [TestCase("System", ChatHistoryCategory.System)]
        [TestCase("Status", ChatHistoryCategory.System)]
        public void GetDefaultCategory_MapsKnownSenders(string sender, ChatHistoryCategory expected)
        {
            Assert.That(ChatHistoryItemFactory.GetDefaultCategory(sender), Is.EqualTo(expected));
        }

        [Test]
        public void Create_NormalizesDisplayTextButKeepsFullTextLineBreaks()
        {
            var item = ChatHistoryItemFactory.Create("System", "line 1\r\nline 2", ChatHistoryCategory.System);
            var prefix = UiLocalization.Text("MainForm.Chat.Prefix.System", "System:");

            Assert.That(item.Sender, Is.EqualTo("System"));
            Assert.That(item.Category, Is.EqualTo(ChatHistoryCategory.System));
            Assert.That(item.DisplayText, Is.EqualTo(prefix + " line 1  line 2"));
            Assert.That(item.FullText, Is.EqualTo(prefix + " line 1\r\nline 2"));
            Assert.That(item.MarkdownText, Is.EqualTo("line 1\r\nline 2"));
        }

        [Test]
        public void Create_UsesLocalizedKnownPrefixes()
        {
            Assert.That(ChatHistoryItemFactory.Create("Me", "hello", ChatHistoryCategory.Conversation).DisplayText, Is.EqualTo(WithPrefix("MainForm.Chat.Prefix.User", "Me:", "hello")));
            Assert.That(ChatHistoryItemFactory.Create("codex", "hello", ChatHistoryCategory.Conversation).DisplayText, Is.EqualTo(WithPrefix("MainForm.Chat.Prefix.Codex", "codex:", "hello")));
            Assert.That(ChatHistoryItemFactory.Create("Review", "hello", ChatHistoryCategory.Conversation).DisplayText, Is.EqualTo(WithPrefix("MainForm.Chat.Prefix.Review", "Review:", "hello")));
            Assert.That(ChatHistoryItemFactory.Create("Status", "hello", ChatHistoryCategory.System).DisplayText, Is.EqualTo(WithPrefix("MainForm.Chat.Prefix.Status", "Status:", "hello")));
            Assert.That(ChatHistoryItemFactory.Create("Error", "hello", ChatHistoryCategory.Error).DisplayText, Is.EqualTo(WithPrefix("MainForm.Chat.Prefix.Error", "Error:", "hello")));
        }

        [Test]
        public void Create_UsesDefaultPrefixForUnknownSender()
        {
            var item = ChatHistoryItemFactory.Create("Tool", "done", ChatHistoryCategory.System);

            Assert.That(item.DisplayText, Is.EqualTo("Tool" + UiLocalization.Text("MainForm.Chat.Prefix.Separator", ":") + " done"));
        }

        [Test]
        public void SenderTextHelpers_ReturnStableFallbacks()
        {
            Assert.That(ChatHistoryItemFactory.GetSystemSenderText(), Is.EqualTo(UiLocalization.Text("MainForm.Chat.Sender.System", "System")));
            Assert.That(ChatHistoryItemFactory.GetUserSenderText(), Is.EqualTo(UiLocalization.Text("MainForm.Chat.Sender.User", "Me")));
            Assert.That(ChatHistoryItemFactory.GetReviewSenderText(), Is.EqualTo(UiLocalization.Text("MainForm.Chat.Sender.Review", "Review")));
            Assert.That(ChatHistoryItemFactory.GetCodexSenderText(), Is.EqualTo("codex"));
            Assert.That(ChatHistoryItemFactory.GetStatusSenderText(), Is.EqualTo(UiLocalization.Text("MainForm.Chat.Sender.Status", "Status")));
            Assert.That(ChatHistoryItemFactory.GetErrorSenderText(), Is.EqualTo(UiLocalization.Text("MainForm.Chat.Sender.Error", "Error")));
        }

        [Test]
        public void Create_CodexDisplayUsesSingleLineButAccessibleTextPreservesReadableLineBreaks()
        {
            var message = "# Title " + char.ConvertFromUtf32(0x2705) + System.Environment.NewLine + "**Bold**";
            var item = ChatHistoryItemFactory.Create("codex", message, ChatHistoryCategory.Conversation);
            var prefix = UiLocalization.Text("MainForm.Chat.Prefix.Codex", "codex:");

            Assert.That(item.DisplayText, Is.EqualTo(prefix + " Title Bold"));
            Assert.That(item.AccessibleText, Is.EqualTo(prefix + " Title" + System.Environment.NewLine + "Bold"));
            Assert.That(item.FullText, Is.EqualTo(prefix + " " + message));
            Assert.That(item.MarkdownText, Is.EqualTo(message));
        }

        [Test]
        public void Create_SystemDisplayTextKeepsMarkdownAndEmoji()
        {
            var message = "# Status " + char.ConvertFromUtf32(0x2705);
            var item = ChatHistoryItemFactory.Create("System", message, ChatHistoryCategory.System);
            var prefix = UiLocalization.Text("MainForm.Chat.Prefix.System", "System:");

            Assert.That(item.DisplayText, Is.EqualTo(prefix + " " + message));
            Assert.That(item.AccessibleText, Is.EqualTo(prefix + " " + message));
            Assert.That(item.MarkdownText, Is.EqualTo(message));
        }

        private static string WithPrefix(string key, string fallback, string message)
        {
            return UiLocalization.Text(key, fallback) + " " + message;
        }
    }
}
