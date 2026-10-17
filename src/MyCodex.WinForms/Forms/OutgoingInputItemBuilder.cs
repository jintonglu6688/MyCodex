using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class OutgoingInputItemBuilder
    {
        internal static List<AppServerUserInputItem> Build(string text, IReadOnlyList<PendingAttachmentItem> attachments, Func<PendingAttachmentItem, byte[]> imageByteReader)
        {
            var inputItems = new List<AppServerUserInputItem>();
            var composeText = text ?? string.Empty;
            var orderedMarkers = BuildOrderedAttachmentMarkers(composeText, attachments);
            var currentIndex = 0;

            foreach (var marker in orderedMarkers)
            {
                if (marker.Index > currentIndex)
                {
                    AppendTextInputItem(inputItems, composeText.Substring(currentIndex, marker.Index - currentIndex));
                }

                AppendAttachmentInputItem(inputItems, marker.Attachment, imageByteReader);
                currentIndex = marker.Index + marker.Attachment.MarkerText.Length;
            }

            if (currentIndex < composeText.Length)
            {
                AppendTextInputItem(inputItems, composeText.Substring(currentIndex));
            }

            return inputItems;
        }

        internal static string BuildPreviewText(string text, IReadOnlyList<PendingAttachmentItem> attachments)
        {
            var previewText = (text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(previewText))
            {
                return previewText;
            }

            if (attachments == null || attachments.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(
                " ",
                attachments
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.MarkerText))
                    .Select(item => item.MarkerText)
                    .ToArray());
        }

        private static List<AttachmentMarkerInfo> BuildOrderedAttachmentMarkers(string composeText, IReadOnlyList<PendingAttachmentItem> attachments)
        {
            var markers = new List<AttachmentMarkerInfo>();
            if (attachments == null)
            {
                return markers;
            }

            foreach (var attachment in attachments)
            {
                if (attachment == null || string.IsNullOrWhiteSpace(attachment.MarkerText))
                {
                    continue;
                }

                var markerIndex = composeText.IndexOf(attachment.MarkerText, StringComparison.Ordinal);
                if (markerIndex < 0)
                {
                    continue;
                }

                markers.Add(new AttachmentMarkerInfo(markerIndex, attachment));
            }

            markers.Sort((left, right) =>
            {
                var result = left.Index.CompareTo(right.Index);
                if (result != 0)
                {
                    return result;
                }

                return string.Compare(
                    left.Attachment != null ? left.Attachment.MarkerText : null,
                    right.Attachment != null ? right.Attachment.MarkerText : null,
                    StringComparison.OrdinalIgnoreCase);
            });
            return markers;
        }

        private static void AppendTextInputItem(List<AppServerUserInputItem> inputItems, string text)
        {
            if (inputItems == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            inputItems.Add(new AppServerUserInputItem
            {
                Type = "text",
                Text = text
            });
        }

        private static void AppendAttachmentInputItem(List<AppServerUserInputItem> inputItems, PendingAttachmentItem attachment, Func<PendingAttachmentItem, byte[]> imageByteReader)
        {
            if (inputItems == null || attachment == null || string.IsNullOrWhiteSpace(attachment.LocalPath))
            {
                return;
            }

            if (attachment.Kind == AttachmentKind.Skill)
            {
                inputItems.Add(new AppServerUserInputItem
                {
                    Type = "skill",
                    Name = string.IsNullOrWhiteSpace(attachment.InputName) ? attachment.DisplayName : attachment.InputName,
                    Path = attachment.LocalPath
                });
                return;
            }

            if (attachment.Kind == AttachmentKind.Plugin)
            {
                inputItems.Add(new AppServerUserInputItem
                {
                    Type = "mention",
                    Name = string.IsNullOrWhiteSpace(attachment.InputName) ? attachment.DisplayName : attachment.InputName,
                    Path = attachment.LocalPath
                });
                return;
            }

            if (attachment.Kind == AttachmentKind.File)
            {
                AppendTextInputItem(inputItems, attachment.LocalPath);
                return;
            }

            var imageDataUrl = BuildImageAttachmentDataUrl(attachment, imageByteReader);
            if (!string.IsNullOrWhiteSpace(imageDataUrl))
            {
                inputItems.Add(new AppServerUserInputItem
                {
                    Type = "image",
                    Url = imageDataUrl
                });
            }
        }

        private static string BuildImageAttachmentDataUrl(PendingAttachmentItem attachment, Func<PendingAttachmentItem, byte[]> imageByteReader)
        {
            var imageBytes = imageByteReader != null ? imageByteReader(attachment) : null;
            if (imageBytes == null || imageBytes.Length == 0)
            {
                throw new InvalidOperationException(string.Format(
                    UiLocalization.Text("MainForm.Exception.ImageAttachmentUnreadable.Template", "Image \"{0}\" is currently unreadable. Please re-add it before sending."),
                    attachment != null
                        ? attachment.DisplayName ?? UiLocalization.Text("MainForm.Attachment.Image.Unnamed", "Unnamed image")
                        : UiLocalization.Text("MainForm.Attachment.Image.Unnamed", "Unnamed image")));
            }

            return "data:" + GetImageMimeType(attachment) + ";base64," + Convert.ToBase64String(imageBytes);
        }

        private static string GetImageMimeType(PendingAttachmentItem attachment)
        {
            var extension = Path.GetExtension(attachment != null ? attachment.LocalPath : null);
            switch ((extension ?? string.Empty).Trim().ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".gif":
                    return "image/gif";
                case ".bmp":
                    return "image/bmp";
                case ".webp":
                    return "image/webp";
                default:
                    return "image/png";
            }
        }

        private sealed class AttachmentMarkerInfo
        {
            public AttachmentMarkerInfo(int index, PendingAttachmentItem attachment)
            {
                Index = index;
                Attachment = attachment;
            }

            public int Index { get; }

            public PendingAttachmentItem Attachment { get; }
        }
    }
}
