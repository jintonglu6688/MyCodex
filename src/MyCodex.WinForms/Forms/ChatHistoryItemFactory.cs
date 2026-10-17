using System;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class ChatHistoryItemFactory
    {
        internal static string GetSystemSenderText()
        {
            return UiLocalization.Text("MainForm.Chat.Sender.System", "System");
        }

        internal static string GetUserSenderText()
        {
            return UiLocalization.Text("MainForm.Chat.Sender.User", "Me");
        }

        internal static string GetReviewSenderText()
        {
            return UiLocalization.Text("MainForm.Chat.Sender.Review", "Review");
        }

        internal static string GetCodexSenderText()
        {
            return "codex";
        }

        internal static string GetStatusSenderText()
        {
            return UiLocalization.Text("MainForm.Chat.Sender.Status", "Status");
        }

        internal static string GetErrorSenderText()
        {
            return UiLocalization.Text("MainForm.Chat.Sender.Error", "Error");
        }

        internal static bool IsUserSender(string sender)
        {
            return IsLegacySender(sender, GetUserSenderText(), "\u7528\u6237") ||
                   string.Equals(sender, "User", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sender, "Me", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sender, "\u6211", StringComparison.Ordinal);
        }

        internal static ChatHistoryCategory GetDefaultCategory(string sender)
        {
            if (IsUserSender(sender) ||
                IsCodexSender(sender) ||
                IsReviewSender(sender))
            {
                return ChatHistoryCategory.Conversation;
            }

            return IsErrorSender(sender)
                ? ChatHistoryCategory.Error
                : ChatHistoryCategory.System;
        }

        internal static ChatHistoryItem Create(string sender, string message)
        {
            return Create(sender, message, GetDefaultCategory(sender));
        }

        internal static ChatHistoryItem Create(string sender, string message, ChatHistoryCategory category)
        {
            string prefix;
            if (IsSystemSender(sender))
            {
                prefix = UiLocalization.Text("MainForm.Chat.Prefix.System", "System:");
            }
            else if (IsUserSender(sender))
            {
                prefix = UiLocalization.Text("MainForm.Chat.Prefix.User", "Me:");
            }
            else if (IsCodexSender(sender))
            {
                prefix = UiLocalization.Text("MainForm.Chat.Prefix.Codex", "codex:");
            }
            else if (IsReviewSender(sender))
            {
                prefix = UiLocalization.Text("MainForm.Chat.Prefix.Review", "Review:");
            }
            else if (IsStatusSender(sender))
            {
                prefix = UiLocalization.Text("MainForm.Chat.Prefix.Status", "Status:");
            }
            else if (IsErrorSender(sender))
            {
                prefix = UiLocalization.Text("MainForm.Chat.Prefix.Error", "Error:");
            }
            else
            {
                prefix = BuildDefaultSenderPrefix(sender);
            }

            var normalized = message ?? string.Empty;
            var shouldSanitize = IsCodexSender(sender);
            var displayMessage = shouldSanitize
                ? ChatMessageTextSanitizer.SanitizeForDisplay(normalized)
                : normalized.Replace("\r", " ").Replace("\n", " ");
            var accessibleMessage = shouldSanitize
                ? ChatMessageTextSanitizer.SanitizeForReadableText(normalized)
                : normalized;
            var displayText = string.Format("{0} {1}", prefix, displayMessage).Trim();
            var fullText = string.Format("{0} {1}", prefix, normalized).Trim();
            var accessibleText = string.Format("{0} {1}", prefix, accessibleMessage).Trim();
            return new ChatHistoryItem
            {
                Sender = sender,
                Category = category,
                DisplayText = displayText,
                FullText = fullText,
                AccessibleText = shouldSanitize ? accessibleText : fullText,
                MarkdownText = normalized
            };
        }

        private static bool IsLegacySender(string sender, string localizedSender, string legacyUnicodeEscapedSender)
        {
            return string.Equals(sender, localizedSender, StringComparison.Ordinal) ||
                   string.Equals(sender, legacyUnicodeEscapedSender, StringComparison.Ordinal);
        }

        private static bool IsSystemSender(string sender)
        {
            return IsLegacySender(sender, GetSystemSenderText(), "\u7cfb\u7edf") ||
                   string.Equals(sender, "System", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReviewSender(string sender)
        {
            return IsLegacySender(sender, GetReviewSenderText(), "\u5ba1\u6838") ||
                   string.Equals(sender, "Review", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStatusSender(string sender)
        {
            return IsLegacySender(sender, GetStatusSenderText(), "\u72b6\u6001") ||
                   string.Equals(sender, "Status", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsErrorSender(string sender)
        {
            return IsLegacySender(sender, GetErrorSenderText(), "\u9519\u8BEF") ||
                   string.Equals(sender, "Error", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCodexSender(string sender)
        {
            return string.Equals(sender, GetCodexSenderText(), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sender, "Codex", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildDefaultSenderPrefix(string sender)
        {
            return sender + UiLocalization.Text("MainForm.Chat.Prefix.Separator", ":");
        }
    }
}
