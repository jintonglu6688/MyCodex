using System;
using System.Collections.Generic;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ComposeDraftStore
    {
        private readonly Dictionary<string, string> _sessionDraftTexts;
        private readonly Dictionary<string, List<PendingAttachmentItem>> _sessionPendingAttachments;

        public ComposeDraftStore()
        {
            _sessionDraftTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sessionPendingAttachments = new Dictionary<string, List<PendingAttachmentItem>>(StringComparer.OrdinalIgnoreCase);
        }

        internal void Save(string sessionId, string draftText, IEnumerable<PendingAttachmentItem> attachments)
        {
            Save(string.Empty, sessionId, draftText, attachments);
        }

        internal void Save(string scopeKey, string sessionId, string draftText, IEnumerable<PendingAttachmentItem> attachments)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            var key = BuildKey(scopeKey, sessionId);
            _sessionDraftTexts[key] = draftText ?? string.Empty;

            var clonedAttachments = ComposeAttachmentState.CloneAttachments(attachments);
            if (clonedAttachments.Count == 0)
            {
                _sessionPendingAttachments.Remove(key);
                return;
            }

            _sessionPendingAttachments[key] = clonedAttachments;
        }

        internal ComposeDraftState Get(string sessionId)
        {
            return Get(string.Empty, sessionId);
        }

        internal ComposeDraftState Get(string scopeKey, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            var key = BuildKey(scopeKey, sessionId);
            string draftText;
            List<PendingAttachmentItem> attachments;
            _sessionDraftTexts.TryGetValue(key, out draftText);
            _sessionPendingAttachments.TryGetValue(key, out attachments);

            if (draftText == null && attachments == null)
            {
                return null;
            }

            return new ComposeDraftState(draftText ?? string.Empty, ComposeAttachmentState.CloneAttachments(attachments));
        }

        internal void Remove(string sessionId, bool deleteTemporaryFiles, Action<PendingAttachmentItem> deleteTemporaryAttachmentFile)
        {
            Remove(string.Empty, sessionId, deleteTemporaryFiles, deleteTemporaryAttachmentFile);
        }

        internal void Remove(string scopeKey, string sessionId, bool deleteTemporaryFiles, Action<PendingAttachmentItem> deleteTemporaryAttachmentFile)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            var key = BuildKey(scopeKey, sessionId);
            _sessionDraftTexts.Remove(key);

            List<PendingAttachmentItem> attachments;
            if (!_sessionPendingAttachments.TryGetValue(key, out attachments))
            {
                return;
            }

            if (deleteTemporaryFiles && deleteTemporaryAttachmentFile != null)
            {
                foreach (var item in attachments)
                {
                    deleteTemporaryAttachmentFile(item);
                }
            }

            _sessionPendingAttachments.Remove(key);
        }

        internal void ClearAll(bool deleteTemporaryFiles, Action<PendingAttachmentItem> deleteTemporaryAttachmentFile)
        {
            if (deleteTemporaryFiles && deleteTemporaryAttachmentFile != null)
            {
                foreach (var attachments in _sessionPendingAttachments.Values)
                {
                    if (attachments == null)
                    {
                        continue;
                    }

                    foreach (var item in attachments)
                    {
                        deleteTemporaryAttachmentFile(item);
                    }
                }
            }

            _sessionDraftTexts.Clear();
            _sessionPendingAttachments.Clear();
        }

        private static string BuildKey(string scopeKey, string sessionId)
        {
            return (scopeKey ?? string.Empty).Trim() + "\n" + (sessionId ?? string.Empty).Trim();
        }
    }
}
