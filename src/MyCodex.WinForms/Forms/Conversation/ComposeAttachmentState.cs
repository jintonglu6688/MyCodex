using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class ComposeAttachmentState
    {
        internal static ComposeAttachmentSyncResult SynchronizeWithInputText(List<PendingAttachmentItem> attachments, string inputText)
        {
            if (attachments == null || attachments.Count == 0)
            {
                return new ComposeAttachmentSyncResult(Array.Empty<PendingAttachmentItem>(), false);
            }

            inputText = inputText ?? string.Empty;
            var removedAttachments = new List<PendingAttachmentItem>();
            var changed = false;

            for (var i = attachments.Count - 1; i >= 0; i--)
            {
                var attachment = attachments[i];
                if (attachment == null || string.IsNullOrWhiteSpace(attachment.MarkerText))
                {
                    continue;
                }

                if (inputText.IndexOf(attachment.MarkerText, StringComparison.Ordinal) >= 0)
                {
                    continue;
                }

                removedAttachments.Add(attachment);
                attachments.RemoveAt(i);
                changed = true;
            }

            if (SortByInputText(attachments, inputText))
            {
                changed = true;
            }

            removedAttachments.Reverse();
            return new ComposeAttachmentSyncResult(removedAttachments, changed);
        }

        internal static bool SortByInputText(List<PendingAttachmentItem> attachments, string inputText)
        {
            if (attachments == null || attachments.Count <= 1)
            {
                return false;
            }

            inputText = inputText ?? string.Empty;
            var originalOrder = attachments.ToArray();
            attachments.Sort((left, right) =>
            {
                var leftIndex = GetAttachmentMarkerIndex(inputText, left);
                var rightIndex = GetAttachmentMarkerIndex(inputText, right);
                if (leftIndex == rightIndex)
                {
                    return string.Compare(left != null ? left.MarkerText : null, right != null ? right.MarkerText : null, StringComparison.OrdinalIgnoreCase);
                }

                if (leftIndex < 0)
                {
                    return 1;
                }

                if (rightIndex < 0)
                {
                    return -1;
                }

                return leftIndex.CompareTo(rightIndex);
            });

            for (var i = 0; i < originalOrder.Length; i++)
            {
                if (!ReferenceEquals(originalOrder[i], attachments[i]))
                {
                    return true;
                }
            }

            return false;
        }

        internal static int GetAttachmentMarkerIndex(string inputText, PendingAttachmentItem attachment)
        {
            if (attachment == null || string.IsNullOrWhiteSpace(attachment.MarkerText) || string.IsNullOrEmpty(inputText))
            {
                return -1;
            }

            return inputText.IndexOf(attachment.MarkerText, StringComparison.Ordinal);
        }

        internal static string BuildAttachmentMarkerText(IEnumerable<PendingAttachmentItem> attachments, AttachmentKind kind, string displayName, string path, bool isDirectory = false)
        {
            var label = GetAttachmentKindLabel(kind, path, isDirectory);
            var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
                ? UiLocalization.Text("MainForm.Attachment.Value.Unnamed", "Unnamed")
                : displayName.Trim();
            var baseMarker = string.Format(
                UiLocalization.Text("MainForm.Attachment.Marker.Template", "[{0}: {1}]"),
                label,
                normalizedDisplayName);
            if (!ContainsAttachmentMarker(attachments, baseMarker))
            {
                return baseMarker;
            }

            for (var index = 2; index < 1000; index++)
            {
                var candidate = string.Format(
                    UiLocalization.Text("MainForm.Attachment.Marker.IndexedTemplate", "[{0}: {1} #{2}]"),
                    label,
                    normalizedDisplayName,
                    index);
                if (!ContainsAttachmentMarker(attachments, candidate))
                {
                    return candidate;
                }
            }

            return baseMarker + " *";
        }

        internal static string GetAttachmentKindLabel(AttachmentKind kind, string path, bool isDirectory = false)
        {
            switch (kind)
            {
                case AttachmentKind.Image:
                    return UiLocalization.Text("MainForm.Attachment.Kind.Image", "Image");
                case AttachmentKind.Skill:
                    return UiLocalization.Text("MainForm.Attachment.Kind.Skill", "Skill");
                case AttachmentKind.Plugin:
                    return UiLocalization.Text("MainForm.Attachment.Kind.Plugin", "Plugin");
                default:
                    if (isDirectory || (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)))
                    {
                        return UiLocalization.Text("MainForm.Attachment.Kind.Folder", "Folder");
                    }

                    return UiLocalization.Text("MainForm.Attachment.Kind.File", "File");
            }
        }

        internal static string GetSkillInsertionDisplayName(AppServerSkillInfo skill)
        {
            if (skill == null)
            {
                return UiLocalization.Text("MainForm.Attachment.Skill.Unnamed", "Unnamed skill");
            }

            if (!string.IsNullOrWhiteSpace(skill.DisplayName))
            {
                return skill.DisplayName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(skill.Name))
            {
                return skill.Name.Trim();
            }

            return UiLocalization.Text("MainForm.Attachment.Skill.Unnamed", "Unnamed skill");
        }

        internal static string GetPluginInsertionDisplayName(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return UiLocalization.Text("MainForm.Attachment.Plugin.Unnamed", "Unnamed plugin");
            }

            if (!string.IsNullOrWhiteSpace(plugin.DisplayName))
            {
                return plugin.DisplayName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(plugin.Name))
            {
                return plugin.Name.Trim();
            }

            return UiLocalization.Text("MainForm.Attachment.Plugin.Unnamed", "Unnamed plugin");
        }

        internal static bool ContainsAttachmentMarker(IEnumerable<PendingAttachmentItem> attachments, string markerText)
        {
            if (string.IsNullOrWhiteSpace(markerText) || attachments == null)
            {
                return false;
            }

            foreach (var attachment in attachments)
            {
                if (attachment != null && string.Equals(attachment.MarkerText, markerText, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ContainsPendingAttachment(IEnumerable<PendingAttachmentItem> attachments, string path)
        {
            if (attachments == null)
            {
                return false;
            }

            foreach (var item in attachments)
            {
                if (string.Equals(item != null ? item.LocalPath : null, path, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        internal static int CountAttachmentsByKind(IEnumerable<PendingAttachmentItem> attachments, AttachmentKind kind)
        {
            return (attachments ?? Array.Empty<PendingAttachmentItem>()).Count(item => item != null && item.Kind == kind);
        }

        internal static int CountFileAttachments(IEnumerable<PendingAttachmentItem> attachments)
        {
            return (attachments ?? Array.Empty<PendingAttachmentItem>()).Count(item => item != null && item.Kind == AttachmentKind.File && !IsFolderAttachment(item));
        }

        internal static int CountFolderAttachments(IEnumerable<PendingAttachmentItem> attachments)
        {
            return (attachments ?? Array.Empty<PendingAttachmentItem>()).Count(IsFolderAttachment);
        }

        internal static bool IsFolderAttachment(PendingAttachmentItem item)
        {
            return item != null &&
                   item.Kind == AttachmentKind.File &&
                   (item.IsDirectory ||
                    (!string.IsNullOrWhiteSpace(item.LocalPath) && Directory.Exists(item.LocalPath)));
        }

        internal static string BuildAttachmentChangeMessage(string actionPrefix, int imageCount, int fileCount, int skillCount = 0, int folderCount = 0, int pluginCount = 0)
        {
            var parts = new List<string>();
            if (imageCount > 0)
            {
                parts.Add(string.Format(UiLocalization.Text("MainForm.Attachment.Change.ImageCount.Template", "{0} image(s)"), imageCount));
            }

            if (fileCount > 0)
            {
                parts.Add(string.Format(UiLocalization.Text("MainForm.Attachment.Change.FileCount.Template", "{0} file(s)"), fileCount));
            }

            if (folderCount > 0)
            {
                parts.Add(string.Format(UiLocalization.Text("MainForm.Attachment.Change.FolderCount.Template", "{0} folder(s)"), folderCount));
            }

            if (skillCount > 0)
            {
                parts.Add(string.Format(UiLocalization.Text("MainForm.Attachment.Change.SkillCount.Template", "{0} skill(s)"), skillCount));
            }

            if (pluginCount > 0)
            {
                parts.Add(string.Format(UiLocalization.Text("MainForm.Attachment.Change.PluginCount.Template", "{0} plugin(s)"), pluginCount));
            }

            return string.Format(
                UiLocalization.Text("MainForm.Attachment.Change.Summary.Template", "{0} {1}"),
                actionPrefix,
                string.Join(UiLocalization.Text("MainForm.Attachment.Change.Separator", ", "), parts)).Trim();
        }

        internal static ComposeDraftState CaptureDraftState(string draftText, IEnumerable<PendingAttachmentItem> attachments)
        {
            return new ComposeDraftState(draftText ?? string.Empty, CloneAttachments(attachments));
        }

        internal static List<PendingAttachmentItem> CloneAttachments(IEnumerable<PendingAttachmentItem> attachments)
        {
            var clones = new List<PendingAttachmentItem>();
            if (attachments == null)
            {
                return clones;
            }

            foreach (var item in attachments)
            {
                if (item != null)
                {
                    clones.Add(item.Clone());
                }
            }

            return clones;
        }
    }
}
