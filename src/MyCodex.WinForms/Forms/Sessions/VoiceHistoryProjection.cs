using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    internal static class VoiceHistoryProjection
    {
        // Ordinary history remains in its existing order. Canonical voice records are inserted before
        // the next ordinary message on the same runtime timeline; the latest voice tail follows it.
        public static List<ChatHistoryItem> Merge(List<ChatHistoryItem> ordinary, RealtimeVoiceContextPage page,
            Func<string, string, ChatHistoryCategory, ChatHistoryItem> create)
        {
            if (page?.Items == null || page.Items.Count == 0) return ordinary;
            var before = new Dictionary<string, List<RealtimeVoiceContextItem>>(StringComparer.Ordinal);
            var pending = new List<RealtimeVoiceContextItem>();
            var categories = new Dictionary<string, ChatHistoryCategory>(StringComparer.Ordinal);
            var presentation = new VoiceReplyPresentation();
            string voiceSession = null;
            foreach (var entry in page.Items)
            {
                if (entry.IsVoice && !VoiceReplyPresentation.HasSpeechContent(entry.Text)) continue;
                if (voiceSession != entry.RealtimeSessionId) { presentation.NewInput(); voiceSession = entry.RealtimeSessionId; }
                var category = entry.IsVoiceBackend ? presentation.Backend(entry.MessagePhase) : presentation.Transcript(entry.Role);
                if (entry.ItemId != null) categories[entry.ItemId] = category;
                if (entry.IsVoice) pending.Add(entry);
                else if (!string.IsNullOrWhiteSpace(entry.ItemId) && pending.Count > 0)
                {
                    before[entry.ItemId] = pending;
                    pending = new List<RealtimeVoiceContextItem>();
                }
            }
            var result = new List<ChatHistoryItem>(ordinary.Count + page.Items.Count);
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            Action<RealtimeVoiceContextItem> append = message =>
            {
                if (string.IsNullOrWhiteSpace(message.ItemId) || !emitted.Add(message.ItemId)) return;
                if (!VoiceReplyPresentation.HasSpeechContent(message.Text)) return;
                ChatHistoryCategory category;
                if (!categories.TryGetValue(message.ItemId, out category)) category = ChatHistoryCategory.Conversation;
                var item = create(message.Role == "user" ? "User" : "Codex", message.Text, category);
                item.ThreadId = page.ThreadId;
                item.SourceItemId = message.ItemId;
                item.IsVoiceTranscript = true;
                result.Add(item);
            };
            foreach (var item in ordinary)
            {
                ChatHistoryCategory category;
                if (item.SourceItemId != null && categories.TryGetValue(item.SourceItemId, out category) && category == ChatHistoryCategory.Processing) item.Category = category;
                List<RealtimeVoiceContextItem> voices;
                if (item.SourceItemId != null && before.TryGetValue(item.SourceItemId, out voices))
                {
                    foreach (var voice in voices) append(voice);
                    before.Remove(item.SourceItemId);
                }
                result.Add(item);
            }
            // A filtered ordinary anchor must not make an otherwise valid voice record disappear.
            foreach (var entry in page.Items)
                if (entry.IsVoice && !emitted.Contains(entry.ItemId)) append(entry);
            return result;
        }
    }
}
