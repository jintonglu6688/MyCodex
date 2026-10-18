using System;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private const int AuxiliarySpeechSuppressMilliseconds = 1500;
        private DateTime _suppressAuxiliarySpeechUntil = DateTime.MinValue;

        private bool HandleScreenReaderHotkey(Keys keyData)
        {
            return _mainManualBrowseContext != null &&
                   _mainManualBrowseContext.HandleHotkey(keyData);
        }

        private ManualBrowseContext CreateMainManualBrowseContext()
        {
            return new ManualBrowseContext(new ManualBrowseContextCallbacks
            {
                GetMessages = () => _chatHistoryListBox.Items.OfType<ChatHistoryItem>().ToArray(),
                EnsureMessageVisible = EnsureChatHistoryItemVisible,
                GetTerminalLines = GetReadableTerminalLines,
                GetActivityItems = GetReadableActivityItems,
                OpenMessageDetail = OpenMainChatMessageDetail,
                PrepareManualReadOutput = PrepareManualReadOutput,
                SpeakManualText = SpeakManualTextOrSetStatus,
                SetStatus = SetStatus,
                AllowLongMessageSpeech = ShouldAllowLongChatSpeechText
            });
        }

        private string[] GetReadableTerminalLines()
        {
            return ManualReadTargetState.GetReadableTerminalLines(_commandTerminalBuffer);
        }

        private string[] GetReadableActivityItems()
        {
            return _activityBrowseControl == null
                ? Array.Empty<string>()
                : _activityBrowseControl.GetReadableItems();
        }

        private void EnsureChatHistoryItemVisible(int index)
        {
            if (index < 0 || index >= _chatHistoryListBox.Items.Count)
            {
                return;
            }

            try
            {
                _chatHistoryListBox.TopIndex = index;
            }
            catch
            {
            }
        }

        private void OpenCurrentChatMessageViewer()
        {
            var item = GetPrimaryChatHistoryItem();
            if (item == null)
            {
                return;
            }

            OpenMainChatMessageDetail(item);
        }

        private void OpenMainChatMessageDetail(ChatHistoryItem item)
        {
            try
            {
                OpenChatMessageDetail(item);
            }
            catch (Exception ex)
            {
                StructuredMessageViewerForm.MessageViewerDiagnostics.LogException("Creating message viewer failed.", ex);
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.ScreenReader.Message.ViewerInitFailed", "Message viewer initialization failed."),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OpenChatMessageDetail(ChatHistoryItem item)
        {
            ChatMessageViewerLauncher.Show(this, item, GetConfiguredMessageDetailPreviewMode(), SetStatus);
        }

        private string GetConfiguredMessageDetailPreviewMode()
        {
            return MessageDetailPreviewMode.Normalize(_appConfigSnapshot == null ? null : _appConfigSnapshot.MessageDetailPreviewMode);
        }

        private void ToggleAutomaticChatReading()
        {
            if (_appConfigSnapshot == null)
            {
                return;
            }

            var isEnabled = !_appConfigSnapshot.EnableAutomaticChatReading;
            if (isEnabled && !EnsureScreenReaderReady())
            {
                SetStatus(string.IsNullOrWhiteSpace(_screenReaderService.LastError)
                    ? UiLocalization.Text("MainForm.ScreenReader.Status.ServiceUnavailable", "Screen reader service unavailable")
                    : _screenReaderService.LastError);
                return;
            }

            _appConfigSnapshot.EnableAutomaticChatReading = isEnabled;
            if (!SaveAppConfig())
            {
                _appConfigSnapshot.EnableAutomaticChatReading = !isEnabled;
                return;
            }

            UpdateChatHistorySpeechMenuState();
            SetStatus(isEnabled
                ? UiLocalization.Text("MainForm.ScreenReader.Status.AutoReadEnabled", "Automatic reading enabled")
                : UiLocalization.Text("MainForm.ScreenReader.Status.AutoReadDisabled", "Automatic reading disabled"));
        }

        private void UpdateChatHistorySpeechMenuState()
        {
            _enableAutoReadMenuItem.Enabled = _screenReaderService.IsAvailable;
            _enableAutoReadMenuItem.Checked = _appConfigSnapshot != null && _appConfigSnapshot.EnableAutomaticChatReading;
        }

        private void TryAutoReadIncomingMessage(ChatHistoryItem item, string threadId)
        {
            if (item == null ||
                _appConfigSnapshot == null ||
                !_appConfigSnapshot.EnableAutomaticChatReading ||
                _currentSession == null ||
                string.IsNullOrWhiteSpace(threadId) ||
                !string.Equals(_currentSession.ThreadId, threadId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var text = ChatHistorySpeechFormatter.BuildAutoReadText(item, ShouldAllowLongChatSpeechText());
            if (!SpeakAuxiliaryTextWithScreenReader(text))
            {
                SetStatus(string.IsNullOrWhiteSpace(_screenReaderService.LastError)
                    ? UiLocalization.Text("MainForm.ScreenReader.Status.AutoReadFailed", "Automatic reading failed")
                    : _screenReaderService.LastError);
            }
        }

        private bool ShouldAllowLongChatSpeechText()
        {
            return _appConfigSnapshot != null &&
                   _appConfigSnapshot.ScreenReaderProvider == ScreenReaderProviderKind.BaoYiWebApi;
        }

        private void SpeakScreenReaderStatus(string text)
        {
            SpeakManualTextOrSetStatus(text);
        }

        private void SpeakManualTextOrSetStatus(string text)
        {
            if (!SpeakManualTextWithScreenReader(text))
            {
                SetStatus(string.IsNullOrWhiteSpace(_screenReaderService.LastError)
                    ? UiLocalization.Text("MainForm.ScreenReader.Status.ReadFailed", "Reading failed")
                    : _screenReaderService.LastError);
            }
        }

        private bool SpeakManualTextWithScreenReader(string text)
        {
            if (!EnsureScreenReaderReady())
            {
                return false;
            }

            StopManualScreenReaderOutput();
            return _screenReaderService.Speak(text, false, false, true);
        }

        private bool SpeakAuxiliaryTextWithScreenReader(string text)
        {
            if (IsAuxiliarySpeechSuppressed() || !EnsureScreenReaderReady())
            {
                return true;
            }

            StopAuxiliaryScreenReaderOutput();
            return _screenReaderService.Speak(text, true, false, true);
        }

        private bool EnsureScreenReaderReady()
        {
            return _screenReaderService != null &&
                   _screenReaderService.IsAvailable &&
                   _screenReaderService.EnsureInitialized();
        }

        private void StopOwnedScreenReaderOutput()
        {
            (_screenReaderService as IConfigurableScreenReaderService)?.StopOwnedOutput();
        }

        private void StopAuxiliaryScreenReaderOutput()
        {
            if (_screenReaderService == null || !_screenReaderService.IsAvailable)
            {
                return;
            }

            _screenReaderService.Stop(true);
        }

        private void PrepareManualReadOutput()
        {
            _suppressAuxiliarySpeechUntil = DateTime.Now.AddMilliseconds(AuxiliarySpeechSuppressMilliseconds);
            StopAuxiliaryScreenReaderOutput();
        }

        private void StopManualScreenReaderOutput()
        {
            if (_screenReaderService == null || !_screenReaderService.IsAvailable)
            {
                return;
            }

            _screenReaderService.Stop(false);
        }

        private bool IsAuxiliarySpeechSuppressed()
        {
            return DateTime.Now < _suppressAuxiliarySpeechUntil;
        }

        private void OnEnableAutoReadMenuItemClick(object sender, EventArgs e)
        {
            ToggleAutomaticChatReading();
        }
    }
}
