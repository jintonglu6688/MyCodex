using System;
using System.Linq;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    // ponytail: one small state holder serves the main workbench and detached sessions without a second screen-reader path.
    internal sealed class ManualBrowseContextCallbacks
    {
        internal Func<ChatHistoryItem[]> GetMessages { get; set; }

        internal Action<int> EnsureMessageVisible { get; set; }

        internal Func<string[]> GetTerminalLines { get; set; }

        internal Func<string[]> GetActivityItems { get; set; }

        internal Action<ChatHistoryItem> OpenMessageDetail { get; set; }

        internal Action PrepareManualReadOutput { get; set; }

        internal Action<string> SpeakManualText { get; set; }

        internal Action<string> SetStatus { get; set; }

        internal Func<bool> AllowLongMessageSpeech { get; set; }
    }

    internal sealed class ManualBrowseContext
    {
        private const int PageSize = 5;
        private readonly ManualBrowseContextCallbacks _callbacks;
        private ManualReadTarget _target;
        private int _messageIndex = -1;
        private ChatHistoryItem _messageItem;
        private ChatHistorySelectionSnapshot _messageSnapshot;
        private int _messageLineIndex = -1;
        private ChatHistoryItem _messageLineItem;
        private ChatHistorySelectionSnapshot _messageLineSnapshot;
        private string[] _messageLines = Array.Empty<string>();
        private int _terminalLineIndex = -1;
        private int _activityIndex = -1;

        internal ManualBrowseContext(ManualBrowseContextCallbacks callbacks)
        {
            _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        }

        internal ManualReadTarget Target
        {
            get { return _target; }
        }

        internal bool HandleHotkey(Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.Delete))
            {
                ToggleTarget();
                return true;
            }

            switch (_target)
            {
                case ManualReadTarget.Terminal:
                    return HandleTerminalKey(keyData);
                case ManualReadTarget.Activity:
                    return HandleActivityKey(keyData);
                default:
                    return HandleMessageKey(keyData);
            }
        }

        internal bool HandleTerminalHotkey(Keys keyData)
        {
            if (!IsTerminalBrowseHotkey(keyData))
            {
                return false;
            }

            if (keyData != (Keys.Alt | Keys.Delete))
            {
                _target = ManualReadTarget.Terminal;
            }

            return HandleHotkey(keyData);
        }

        internal void ResetTerminalCursor()
        {
            _terminalLineIndex = -1;
        }

        private bool HandleMessageKey(Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.Enter))
            {
                OpenCurrentMessageDetail();
                return true;
            }

            if (keyData == (Keys.Alt | Keys.Left))
            {
                ReadRelativeMessageLine(-1);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.Right))
            {
                ReadRelativeMessageLine(1);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.Up))
            {
                ReadRelativeMessage(-1, 1);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.Down))
            {
                ReadRelativeMessage(1, 1);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.Home))
            {
                ReadBoundaryMessage(true);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.End))
            {
                ReadBoundaryMessage(false);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.PageUp))
            {
                ReadRelativeMessage(-1, PageSize);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.PageDown))
            {
                ReadRelativeMessage(1, PageSize);
                return true;
            }

            return false;
        }

        private bool HandleTerminalKey(Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.Up))
            {
                ReadRelativeTerminalLine(-1, 1);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.Down))
            {
                ReadRelativeTerminalLine(1, 1);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.Home))
            {
                ReadBoundaryTerminalLine(true);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.End))
            {
                ReadBoundaryTerminalLine(false);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.PageUp))
            {
                ReadRelativeTerminalLine(-1, PageSize);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.PageDown))
            {
                ReadRelativeTerminalLine(1, PageSize);
                return true;
            }

            return false;
        }

        private bool HandleActivityKey(Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.Up))
            {
                ReadRelativeActivityItem(-1, 1);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.Down))
            {
                ReadRelativeActivityItem(1, 1);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.Home))
            {
                ReadBoundaryActivityItem(true);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.End))
            {
                ReadBoundaryActivityItem(false);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.PageUp))
            {
                ReadRelativeActivityItem(-1, PageSize);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.PageDown))
            {
                ReadRelativeActivityItem(1, PageSize);
                return true;
            }

            return false;
        }

        private void ToggleTarget()
        {
            _target = ManualReadTargetState.Toggle(_target);
            var targetName = GetTargetName();
            _callbacks.SetStatus?.Invoke(UiLocalization.Text("MainForm.ScreenReader.Status.ReadTargetPrefix", "Read target: ") + targetName);
            Speak(targetName);
        }

        private string GetTargetName()
        {
            switch (_target)
            {
                case ManualReadTarget.Terminal:
                    return UiLocalization.Text("MainForm.ScreenReader.Target.Terminal", "Terminal");
                case ManualReadTarget.Activity:
                    return UiLocalization.Text("MainForm.ScreenReader.Target.Activity", "Activity");
                default:
                    return UiLocalization.Text("MainForm.ScreenReader.Target.Message", "Message");
            }
        }

        private void ReadRelativeMessage(int direction, int step)
        {
            PrepareManualReadOutput();
            var items = GetMessages();
            if (items.Length <= 0)
            {
                SpeakStatus("MainForm.ScreenReader.Status.NoMessages", "No messages currently.");
                return;
            }

            var currentIndex = GetCurrentMessageIndex(items);
            if (currentIndex < 0 && _messageSnapshot != null)
            {
                SpeakStatus("MainForm.ScreenReader.Status.NoReadableMessages", "No readable messages currently.");
                return;
            }

            var targetIndex = CalculateRelativeIndex(currentIndex, items.Length, direction, step);
            MoveToMessage(items, targetIndex);
        }

        private void ReadBoundaryMessage(bool readFirst)
        {
            PrepareManualReadOutput();
            var items = GetMessages();
            if (items.Length <= 0)
            {
                SpeakStatus("MainForm.ScreenReader.Status.NoMessages", "No messages currently.");
                return;
            }

            MoveToMessage(items, readFirst ? 0 : items.Length - 1);
        }

        private void MoveToMessage(ChatHistoryItem[] items, int requestedIndex)
        {
            var targetIndex = Clamp(requestedIndex, 0, items.Length - 1);
            var item = items[targetIndex];
            if (item == null)
            {
                SpeakStatus("MainForm.ScreenReader.Status.NoReadableMessages", "No readable messages currently.");
                return;
            }

            _target = ManualReadTarget.Message;
            _messageIndex = targetIndex;
            _messageItem = item;
            _messageSnapshot = ChatHistorySelectionSnapshot.Capture(item, targetIndex);
            ResetMessageLineCursor();
            _callbacks.EnsureMessageVisible?.Invoke(targetIndex);
            Speak(ChatHistorySpeechFormatter.BuildBrowseText(item, AllowLongMessageSpeech()));
        }

        private void ReadRelativeMessageLine(int direction)
        {
            PrepareManualReadOutput();
            var items = GetMessages();
            var messageIndex = GetCurrentMessageIndex(items);
            if (messageIndex < 0 || items[messageIndex] == null)
            {
                SpeakStatus("MainForm.ScreenReader.Status.NoReadableMessages", "No readable messages currently.");
                return;
            }

            _target = ManualReadTarget.Message;
            var item = items[messageIndex];
            var preserveLineCursor = _messageLineSnapshot != null && _messageLineSnapshot.Matches(item);
            if (!ReferenceEquals(_messageLineItem, item))
            {
                _messageLineItem = item;
                _messageLines = ChatHistorySpeechFormatter.BuildReadableLines(item)
                    .Select(NormalizeReadableLine)
                    .ToArray();
                if (!preserveLineCursor)
                {
                    _messageLineIndex = 0;
                }
                else if (_messageLineIndex >= 0)
                {
                    _messageLineIndex += direction;
                }
            }
            else if (_messageLineIndex < 0)
            {
                _messageLineIndex = 0;
            }
            else
            {
                _messageLineIndex += direction;
            }

            if (_messageLines.Length <= 0)
            {
                SpeakStatus("MainForm.ScreenReader.Status.MessageHasNoReadableContent", "Current message has no readable content.");
                return;
            }

            _messageLineIndex = Clamp(_messageLineIndex, 0, _messageLines.Length - 1);
            _messageLineSnapshot = ChatHistorySelectionSnapshot.Capture(item, messageIndex);
            Speak(_messageLines[_messageLineIndex]);
        }

        private void OpenCurrentMessageDetail()
        {
            var items = GetMessages();
            var messageIndex = GetCurrentMessageIndex(items);
            if (messageIndex < 0 || items[messageIndex] == null)
            {
                SpeakStatus("MainForm.ScreenReader.Status.NoReadableMessages", "No readable messages currently.");
                return;
            }

            _callbacks.OpenMessageDetail?.Invoke(items[messageIndex]);
        }

        private void ReadRelativeTerminalLine(int direction, int step)
        {
            PrepareManualReadOutput();
            var lines = GetTerminalLines();
            if (lines.Length <= 0)
            {
                SpeakStatus("MainForm.ScreenReader.Status.NoTerminalContent", "No terminal content currently.");
                return;
            }

            _target = ManualReadTarget.Terminal;
            _terminalLineIndex = CalculateRelativeIndex(_terminalLineIndex, lines.Length, direction, step);
            Speak(lines[_terminalLineIndex]);
        }

        private void ReadBoundaryTerminalLine(bool readFirst)
        {
            PrepareManualReadOutput();
            var lines = GetTerminalLines();
            if (lines.Length <= 0)
            {
                SpeakStatus("MainForm.ScreenReader.Status.NoTerminalContent", "No terminal content currently.");
                return;
            }

            _target = ManualReadTarget.Terminal;
            _terminalLineIndex = readFirst ? 0 : lines.Length - 1;
            Speak(lines[_terminalLineIndex]);
        }

        private void ReadRelativeActivityItem(int direction, int step)
        {
            PrepareManualReadOutput();
            var items = GetActivityItems();
            if (items.Length <= 0)
            {
                SpeakStatus("MainForm.ScreenReader.Status.NoActivityContent", "No activity records currently.");
                return;
            }

            _target = ManualReadTarget.Activity;
            _activityIndex = CalculateRelativeIndex(_activityIndex, items.Length, direction, step);
            Speak(items[_activityIndex]);
        }

        private void ReadBoundaryActivityItem(bool readFirst)
        {
            PrepareManualReadOutput();
            var items = GetActivityItems();
            if (items.Length <= 0)
            {
                SpeakStatus("MainForm.ScreenReader.Status.NoActivityContent", "No activity records currently.");
                return;
            }

            _target = ManualReadTarget.Activity;
            _activityIndex = readFirst ? 0 : items.Length - 1;
            Speak(items[_activityIndex]);
        }

        private ChatHistoryItem[] GetMessages()
        {
            return _callbacks.GetMessages != null
                ? _callbacks.GetMessages() ?? Array.Empty<ChatHistoryItem>()
                : Array.Empty<ChatHistoryItem>();
        }

        private string[] GetTerminalLines()
        {
            return _callbacks.GetTerminalLines != null
                ? (_callbacks.GetTerminalLines() ?? Array.Empty<string>())
                    .Select(NormalizeReadableLine)
                    .ToArray()
                : Array.Empty<string>();
        }

        private string[] GetActivityItems()
        {
            return _callbacks.GetActivityItems != null
                ? (_callbacks.GetActivityItems() ?? Array.Empty<string>())
                    .Select(NormalizeReadableLine)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray()
                : Array.Empty<string>();
        }

        private int GetCurrentMessageIndex(ChatHistoryItem[] items)
        {
            if (_messageItem != null &&
                _messageIndex >= 0 &&
                _messageIndex < items.Length &&
                ReferenceEquals(items[_messageIndex], _messageItem))
            {
                return _messageIndex;
            }

            if (_messageSnapshot != null)
            {
                var resolvedIndex = _messageSnapshot.ResolveMatchingIndex(items);
                if (resolvedIndex >= 0)
                {
                    _messageIndex = resolvedIndex;
                    _messageItem = items[resolvedIndex];
                }

                return resolvedIndex;
            }

            return _messageIndex >= 0 && _messageIndex < items.Length ? _messageIndex : -1;
        }

        private void ResetMessageLineCursor()
        {
            _messageLineIndex = -1;
            _messageLineItem = null;
            _messageLineSnapshot = null;
            _messageLines = Array.Empty<string>();
        }

        private void PrepareManualReadOutput()
        {
            _callbacks.PrepareManualReadOutput?.Invoke();
        }

        private void SpeakStatus(string resourceKey, string fallback)
        {
            Speak(UiLocalization.Text(resourceKey, fallback));
        }

        private void Speak(string text)
        {
            _callbacks.SpeakManualText?.Invoke(text);
        }

        private bool AllowLongMessageSpeech()
        {
            return _callbacks.AllowLongMessageSpeech != null && _callbacks.AllowLongMessageSpeech();
        }

        private static int CalculateRelativeIndex(int currentIndex, int count, int direction, int step)
        {
            if (currentIndex < 0 || currentIndex >= count)
            {
                return direction < 0 ? count - 1 : 0;
            }

            return Clamp(currentIndex + (direction * step), 0, count - 1);
        }

        private static bool IsTerminalBrowseHotkey(Keys keyData)
        {
            return keyData == (Keys.Alt | Keys.Delete) ||
                   keyData == (Keys.Alt | Keys.Up) ||
                   keyData == (Keys.Alt | Keys.Down) ||
                   keyData == (Keys.Alt | Keys.Home) ||
                   keyData == (Keys.Alt | Keys.End) ||
                   keyData == (Keys.Alt | Keys.PageUp) ||
                   keyData == (Keys.Alt | Keys.PageDown);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        private static string NormalizeReadableLine(string line)
        {
            return (line ?? string.Empty).TrimEnd();
        }
    }
}
