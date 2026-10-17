using System;
using System.Windows.Forms;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class ChatMessageViewerLauncher
    {
        public static void Show(
            IWin32Window owner,
            ChatHistoryItem item,
            string previewMode,
            Action<string> setStatus)
        {
            if (item == null)
            {
                return;
            }

            var normalizedMode = MessageDetailPreviewMode.Normalize(previewMode);
            if (string.Equals(normalizedMode, MessageDetailPreviewMode.Text, StringComparison.OrdinalIgnoreCase))
            {
                ShowTextViewer(owner, item);
                return;
            }

            if (TryShowWebViewer(owner, item))
            {
                return;
            }

            if (setStatus != null)
            {
                setStatus(UiLocalization.Text("MainForm.ScreenReader.Message.WebViewFallback", "Web message viewer is unavailable. Opened the text viewer instead."));
            }

            ShowTextViewer(owner, item);
        }

        private static bool TryShowWebViewer(IWin32Window owner, ChatHistoryItem item)
        {
            if (!WebMessageViewerForm.IsWebView2RuntimeAvailable())
            {
                return false;
            }

            using (var viewer = new WebMessageViewerForm(item))
            {
                viewer.ShowDialog(owner);
                return !viewer.InitializationFailed;
            }
        }

        private static void ShowTextViewer(IWin32Window owner, ChatHistoryItem item)
        {
            using (var viewer = new StructuredMessageViewerForm(item))
            {
                viewer.ShowDialog(owner);
            }
        }
    }
}
