using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;

namespace MyCodex.WinForms.Controls
{
    internal sealed class ChatWorkbenchHostCallbacks
    {
        public Func<Task<IReadOnlyList<PendingAttachmentItem>>> SelectAttachmentsAsync { get; set; }

        public Func<Task<IReadOnlyList<PendingAttachmentItem>>> SelectImageAttachmentsAsync { get; set; }

        public Func<Task<IReadOnlyList<PendingAttachmentItem>>> SelectFileAttachmentsAsync { get; set; }

        public Func<Task<IReadOnlyList<PendingAttachmentItem>>> SelectFolderAttachmentsAsync { get; set; }

        public Func<Task<IReadOnlyList<PendingAttachmentItem>>> SelectSkillAttachmentsAsync { get; set; }

        public Func<Task<IReadOnlyList<PendingAttachmentItem>>> SelectPluginAttachmentsAsync { get; set; }

        public Func<bool> CanPasteAttachments { get; set; }

        public Func<Task<IReadOnlyList<PendingAttachmentItem>>> PasteAttachmentsAsync { get; set; }

        public Func<IReadOnlyList<PendingAttachmentItem>, Task<IReadOnlyList<PendingAttachmentItem>>> ValidateAttachmentsAsync { get; set; }

        public Func<PendingAttachmentItem, byte[]> ReadImageBytes { get; set; }

        public Func<ChatWorkbenchSendRequest, Task<ChatWorkbenchSendResult>> SendAsync { get; set; }

        public Func<Task> StopAsync { get; set; }

        public Func<bool> CanSend { get; set; }

        public Func<bool> CanStop { get; set; }

        public Func<string> GetSendMessageShortcutMode { get; set; }

        public Action<ChatHistoryItem> OpenMessageDetail { get; set; }

        public Action<string> AnnounceStatus { get; set; }

        public Action<PendingAttachmentItem> DeleteTemporaryAttachment { get; set; }
    }

    internal sealed class ChatWorkbenchSendRequest
    {
        public ChatWorkbenchSendRequest(
            string rawText,
            string previewText,
            IReadOnlyList<AppServerUserInputItem> inputItems,
            IReadOnlyList<PendingAttachmentItem> attachments)
        {
            RawText = rawText ?? string.Empty;
            PreviewText = previewText ?? string.Empty;
            InputItems = inputItems ?? new List<AppServerUserInputItem>();
            Attachments = attachments ?? new List<PendingAttachmentItem>();
        }

        public string RawText { get; }

        public string PreviewText { get; }

        public IReadOnlyList<AppServerUserInputItem> InputItems { get; }

        public IReadOnlyList<PendingAttachmentItem> Attachments { get; }
    }

    internal sealed class ChatWorkbenchSendResult
    {
        public ChatWorkbenchSendResult(bool isSuccess, bool clearInput, string statusText)
            : this(isSuccess, clearInput, false, statusText)
        {
        }

        public ChatWorkbenchSendResult(bool isSuccess, bool clearInput, bool keepSending, string statusText)
        {
            IsSuccess = isSuccess;
            ClearInput = clearInput;
            KeepSending = keepSending;
            StatusText = statusText ?? string.Empty;
        }

        public bool IsSuccess { get; }

        public bool ClearInput { get; }

        public bool KeepSending { get; }

        public string StatusText { get; }
    }
}
