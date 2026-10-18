using System.Collections.Generic;
using System.IO;
using MyCodex.Infrastructure.Persistence;

namespace MyCodex.WinForms.Forms
{
    internal static class PendingAttachmentSendValidator
    {
        internal static List<PendingAttachmentItem> EnsureReady(IReadOnlyList<PendingAttachmentItem> attachments)
        {
            var unavailableAttachments = new List<PendingAttachmentItem>();
            if (attachments == null)
            {
                return unavailableAttachments;
            }

            foreach (var item in attachments)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.LocalPath))
                {
                    continue;
                }

                if (item.IsRemotePath)
                {
                    continue;
                }

                if (PathExists(item.Kind, item.LocalPath))
                {
                    continue;
                }

                string resolvedPath;
                if ((item.Kind == AttachmentKind.Skill || item.Kind == AttachmentKind.Plugin) &&
                    PluginSkillPathResolver.TryResolveCurrentPath(item.LocalPath, out resolvedPath) &&
                    PathExists(item.Kind, resolvedPath))
                {
                    continue;
                }

                TryMaterializeTemporaryImage(item);
                if (!PathExists(item.Kind, item.LocalPath))
                {
                    unavailableAttachments.Add(item);
                }
            }

            return unavailableAttachments;
        }

        internal static bool PathExists(AttachmentKind kind, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (kind == AttachmentKind.Image)
            {
                return File.Exists(path);
            }

            return File.Exists(path) || Directory.Exists(path);
        }

        private static void TryMaterializeTemporaryImage(PendingAttachmentItem item)
        {
            if (item == null ||
                item.Kind != AttachmentKind.Image ||
                !item.IsTemporaryFile ||
                item.InMemoryContent == null ||
                item.InMemoryContent.Length == 0 ||
                string.IsNullOrWhiteSpace(item.LocalPath))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(item.LocalPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(item.LocalPath, item.InMemoryContent);
            }
            catch
            {
            }
        }
    }
}
