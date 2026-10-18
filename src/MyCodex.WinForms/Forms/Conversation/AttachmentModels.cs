using System.Collections.Generic;

namespace MyCodex.WinForms.Forms
{
    internal enum AttachmentKind
    {
        Image,
        File,
        Skill,
        Plugin
    }

    internal sealed class PendingAttachmentItem
    {
        public PendingAttachmentItem(string localPath, string displayName, bool isTemporaryFile, AttachmentKind kind, string markerText, byte[] inMemoryContent, string inputName)
            : this(localPath, displayName, isTemporaryFile, kind, markerText, inMemoryContent, inputName, false, false)
        {
        }

        public PendingAttachmentItem(
            string localPath,
            string displayName,
            bool isTemporaryFile,
            AttachmentKind kind,
            string markerText,
            byte[] inMemoryContent,
            string inputName,
            bool isDirectory,
            bool isRemotePath)
        {
            LocalPath = localPath;
            DisplayName = displayName;
            IsTemporaryFile = isTemporaryFile;
            Kind = kind;
            MarkerText = markerText;
            InMemoryContent = inMemoryContent != null ? (byte[])inMemoryContent.Clone() : null;
            InputName = inputName;
            IsDirectory = isDirectory;
            IsRemotePath = isRemotePath;
        }

        public string LocalPath { get; }

        public string DisplayName { get; }

        public bool IsTemporaryFile { get; }

        public AttachmentKind Kind { get; }

        public string MarkerText { get; }

        public byte[] InMemoryContent { get; }

        public string InputName { get; }

        public bool IsDirectory { get; }

        public bool IsRemotePath { get; }

        public PendingAttachmentItem Clone()
        {
            return new PendingAttachmentItem(LocalPath, DisplayName, IsTemporaryFile, Kind, MarkerText, InMemoryContent, InputName, IsDirectory, IsRemotePath);
        }

        public override string ToString()
        {
            return string.Format("{0}：{1}", ComposeAttachmentState.GetAttachmentKindLabel(Kind, LocalPath, IsDirectory), DisplayName);
        }
    }

    internal sealed class ComposeDraftState
    {
        public ComposeDraftState(string draftText, IReadOnlyList<PendingAttachmentItem> attachments)
        {
            DraftText = draftText ?? string.Empty;
            Attachments = attachments ?? new List<PendingAttachmentItem>();
        }

        public string DraftText { get; }

        public IReadOnlyList<PendingAttachmentItem> Attachments { get; }
    }

    internal sealed class ComposeAttachmentSyncResult
    {
        public ComposeAttachmentSyncResult(IReadOnlyList<PendingAttachmentItem> removedAttachments, bool changed)
        {
            RemovedAttachments = removedAttachments ?? new List<PendingAttachmentItem>();
            Changed = changed;
        }

        public IReadOnlyList<PendingAttachmentItem> RemovedAttachments { get; }

        public bool Changed { get; }
    }
}
