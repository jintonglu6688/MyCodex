using System;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    // Keep the full backend answer. Its spoken paraphrase is supporting output, not another answer.
    internal sealed class VoiceReplyPresentation
    {
        private bool _hasBackendAnswer;

        internal void NewInput() { _hasBackendAnswer = false; }

        internal ChatHistoryCategory Backend(string phase)
        {
            if (string.Equals(phase, "commentary", StringComparison.Ordinal)) return ChatHistoryCategory.Processing;
            _hasBackendAnswer = true;
            return ChatHistoryCategory.Conversation;
        }

        internal ChatHistoryCategory Transcript(string role)
        {
            if (role == "user") NewInput();
            return role == "assistant" && _hasBackendAnswer ? ChatHistoryCategory.Processing : ChatHistoryCategory.Conversation;
        }

        internal static bool HasSpeechContent(string text)
        {
            if (text == null) return false;
            foreach (var value in text)
                if (!char.IsWhiteSpace(value) && !char.IsPunctuation(value)) return true;
            return false;
        }
    }
}
