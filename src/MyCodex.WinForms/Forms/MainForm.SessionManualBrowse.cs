using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly Dictionary<string, ManualBrowseContext> _sessionManualBrowseContexts =
            new Dictionary<string, ManualBrowseContext>(StringComparer.OrdinalIgnoreCase);

        private ManualBrowseContext GetOrCreateSessionManualBrowseContext(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            ManualBrowseContext context;
            if (_sessionManualBrowseContexts.TryGetValue(sessionId, out context))
            {
                return context;
            }

            context = new ManualBrowseContext(new ManualBrowseContextCallbacks
            {
                GetMessages = () =>
                {
                    var form = GetOpenSessionWorkbenchForManualBrowse(sessionId);
                    return form != null ? form.GetManualBrowseHistoryItems() : Array.Empty<ChatHistoryItem>();
                },
                EnsureMessageVisible = index =>
                {
                    var form = GetOpenSessionWorkbenchForManualBrowse(sessionId);
                    form?.EnsureManualBrowseHistoryItemVisible(index);
                },
                GetTerminalLines = () =>
                {
                    var host = GetOpenSessionCommandTerminalForManualBrowse(sessionId);
                    return host != null ? host.GetReadableLines() : Array.Empty<string>();
                },
                GetActivityItems = () =>
                {
                    var form = GetOpenSessionWorkbenchForManualBrowse(sessionId);
                    return form != null ? form.GetManualBrowseActivityItems() : Array.Empty<string>();
                },
                OpenMessageDetail = item =>
                {
                    var form = GetOpenSessionWorkbenchForManualBrowse(sessionId);
                    form?.OpenManualBrowseMessageDetail(item);
                },
                PrepareManualReadOutput = PrepareManualReadOutput,
                SpeakManualText = text => SpeakSessionManualBrowseText(sessionId, text),
                SetStatus = text =>
                {
                    var form = GetOpenSessionWorkbenchForManualBrowse(sessionId);
                    form?.SetManualBrowseStatus(text);
                },
                AllowLongMessageSpeech = ShouldAllowLongChatSpeechText
            });
            _sessionManualBrowseContexts[sessionId] = context;
            return context;
        }

        private SessionWorkbenchForm GetOpenSessionWorkbenchForManualBrowse(string sessionId)
        {
            SessionWorkbenchForm form;
            return _sessionWorkbenches.TryGetValue(sessionId, out form) &&
                   form != null &&
                   !form.IsDisposed &&
                   !form.Disposing
                ? form
                : null;
        }

        private SessionCommandTerminalHost GetOpenSessionCommandTerminalForManualBrowse(string sessionId)
        {
            SessionCommandTerminalHost host;
            return _sessionCommandTerminals.TryGetValue(sessionId, out host) &&
                   host != null &&
                   host.HasOpenWindow
                ? host
                : null;
        }

        private void SpeakSessionManualBrowseText(string sessionId, string text)
        {
            if (SpeakManualTextWithScreenReader(text))
            {
                return;
            }

            var form = GetOpenSessionWorkbenchForManualBrowse(sessionId);
            if (form == null)
            {
                return;
            }

            form.SetManualBrowseStatus(_screenReaderService == null || string.IsNullOrWhiteSpace(_screenReaderService.LastError)
                ? UiLocalization.Text("MainForm.ScreenReader.Status.ReadFailed", "Reading failed")
                : _screenReaderService.LastError);
        }

        private void HandleSessionTerminalScreenReaderHotkey(string sessionId, CommandTerminalHotkeyEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            ManualBrowseContext context;
            e.Handled = _sessionManualBrowseContexts.TryGetValue(sessionId, out context) &&
                        context.HandleTerminalHotkey(e.KeyData);
        }

        private void HandleSessionTerminalBufferReset(string sessionId)
        {
            ManualBrowseContext context;
            if (_sessionManualBrowseContexts.TryGetValue(sessionId, out context))
            {
                context.ResetTerminalCursor();
            }
        }

    }
}
