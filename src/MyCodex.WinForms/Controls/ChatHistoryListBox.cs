using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Controls
{
    public class ChatHistoryItem
    {
        public string ThreadId { get; set; }
        public string SourceItemId { get; set; }
        public bool IsVoiceTranscript { get; set; }

        public string TurnId { get; set; }

        public int TurnIndex { get; set; }

        public int TurnCount { get; set; }

        public string Sender { get; set; }

        public ChatHistoryCategory Category { get; set; }

        public string DisplayText { get; set; }

        public string FullText { get; set; }

        public string AccessibleText { get; set; }

        public string MarkdownText { get; set; }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(AccessibleText))
            {
                return AccessibleText;
            }

            if (!string.IsNullOrWhiteSpace(FullText))
            {
                return FullText;
            }

            return DisplayText ?? string.Empty;
        }
    }

    public sealed class ChatHistorySelectionSummaryEventArgs : EventArgs
    {
        public ChatHistorySelectionSummaryEventArgs(
            int selectedCount,
            int totalCount,
            int startIndex,
            int endIndex,
            int focusedIndex,
            bool isAllSelected,
            string summaryText)
        {
            SelectedCount = selectedCount;
            TotalCount = totalCount;
            StartIndex = startIndex;
            EndIndex = endIndex;
            FocusedIndex = focusedIndex;
            IsAllSelected = isAllSelected;
            SummaryText = summaryText ?? string.Empty;
        }

        public int SelectedCount { get; }

        public int TotalCount { get; }

        public int StartIndex { get; }

        public int EndIndex { get; }

        public int FocusedIndex { get; }

        public bool IsAllSelected { get; }

        public string SummaryText { get; }
    }

    public sealed class ChatHistoryAnchorNavigationEventArgs : EventArgs
    {
        public ChatHistoryAnchorNavigationEventArgs(int direction)
        {
            Direction = direction;
        }

        public int Direction { get; }

        public bool Handled { get; set; }
    }

    public class ChatHistoryListBox : ListBox
    {
        private const int LbSetCaretIndex = 0x019E;
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        private const int TextVerticalPadding = 4;
        private const int MaximumRenderTextLength = 1024;
        private const TextFormatFlags MessageTextFlags = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        private readonly MenuItem _copyMessageMenuItem;
        private readonly HashSet<int> _logicalSelectedIndices;
        private int _twoLineTextHeight;
        private int _focusedIndex = -1;
        private int _selectionAnchorIndex = -1;
        private bool _isLogicalSelectionActive;
        private bool _suppressSelectedIndexChanged;
        private bool _updatingSelection;

        public ChatHistoryListBox()
        {
            _logicalSelectedIndices = new HashSet<int>();
            _copyMessageMenuItem = new MenuItem(UiLocalization.Text("ChatHistoryListBox.Context.CopyMessage", "Copy Message(&C)"));
            _copyMessageMenuItem.Click += delegate { CopySelectionToClipboard(); };

            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.FixedSingle;
            UpdateItemHeight();
            AccessibleRole = AccessibleRole.List;
            SelectionMode = SelectionMode.MultiExtended;
            ContextMenu = new ContextMenu(new[] { _copyMessageMenuItem });
            ContextMenu.Popup += delegate { _copyMessageMenuItem.Enabled = CanCopySelection(); };
        }

        internal event EventHandler<ChatHistorySelectionSummaryEventArgs> SelectionSummaryRequested;

        internal event EventHandler<ChatHistoryAnchorNavigationEventArgs> AnchorNavigationRequested;

        internal int FocusedIndex
        {
            get { return GetFocusedIndex(); }
        }

        internal ChatHistoryItem PrimaryChatHistoryItem
        {
            get
            {
                var index = GetFocusedIndex();
                if (index < 0 || index >= Items.Count)
                {
                    return SelectedItem as ChatHistoryItem;
                }

                return Items[index] as ChatHistoryItem;
            }
        }

        internal ChatHistoryItem[] GetSelectedChatHistoryItemsInDisplayOrder()
        {
            return GetSelectedIndicesInDisplayOrder()
                .Select(index => Items[index] as ChatHistoryItem)
                .Where(item => item != null)
                .ToArray();
        }

        internal string BuildClipboardTextForSelection()
        {
            var selectedItems = GetSelectedChatHistoryItemsInDisplayOrder();
            if (selectedItems.Length == 0)
            {
                var primaryItem = PrimaryChatHistoryItem;
                selectedItems = primaryItem != null
                    ? new[] { primaryItem }
                    : Array.Empty<ChatHistoryItem>();
            }

            var texts = selectedItems
                .Select(item => item.FullText)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();

            return texts.Length == 0
                ? string.Empty
                : string.Join(Environment.NewLine + Environment.NewLine, texts);
        }

        internal bool CopySelectionToClipboard()
        {
            var text = BuildClipboardTextForSelection();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            Clipboard.SetText(text, TextDataFormat.UnicodeText);
            return true;
        }

        internal int[] GetSelectedIndicesInDisplayOrder()
        {
            if (_isLogicalSelectionActive)
            {
                return _logicalSelectedIndices
                    .Where(IsValidIndex)
                    .OrderBy(index => index)
                    .ToArray();
            }

            return SelectedIndices
                .Cast<int>()
                .OrderBy(index => index)
                .ToArray();
        }

        internal void SelectSingleIndex(int index)
        {
            if (!IsValidIndex(index))
            {
                return;
            }

            BeginUpdate();
            _updatingSelection = true;
            try
            {
                ClearLogicalSelection();
                ClearSelected();
                SetSelected(index, true);
                _selectionAnchorIndex = index;
                SetFocusedIndexCore(index, true);
            }
            finally
            {
                _updatingSelection = false;
                EndUpdate();
            }
        }

        internal void FocusIndex(int index)
        {
            if (!IsValidIndex(index))
            {
                return;
            }

            FocusIndexPreservingSelection(index);
        }

        internal bool ProcessExplorerSelectionKeyForTests(Keys keyData)
        {
            return ProcessExplorerSelectionKey(keyData);
        }

        internal bool ProcessCommandKeyForTests(Keys keyData)
        {
            var message = new Message();
            return ProcessCmdKey(ref message, keyData);
        }

        internal bool ProcessKeyMessageForTests(Keys keyData)
        {
            var keyCode = keyData & Keys.KeyCode;
            var message = new Message { Msg = WmKeyDown, WParam = new IntPtr((int)keyCode) };
            return ProcessKeyMessageCore(ref message, keyData & Keys.Modifiers);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateItemHeight();
        }

        private void UpdateItemHeight()
        {
            _twoLineTextHeight = TextRenderer.MeasureText("Ag\nAg", Font, new Size(int.MaxValue, int.MaxValue), MessageTextFlags).Height;
            ItemHeight = _twoLineTextHeight + 2 * UiTheme.Scale(TextVerticalPadding);
        }

        internal static string BuildRenderText(string text)
        {
            text = text ?? string.Empty;
            if (text.Length <= MaximumRenderTextLength) return text;

            // Bound visual layout work; native accessibility, copying and details retain the full text.
            var length = MaximumRenderTextLength - 1;
            if (char.IsHighSurrogate(text[length - 1])) length--;
            return text.Substring(0, length) + "\u2026";
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                base.OnDrawItem(e);
                return;
            }

            var chatItem = Items[e.Index] as ChatHistoryItem;
            var isSelected = IsIndexSelectedForDisplay(e.Index, e.State);
            var isFocused = (e.State & DrawItemState.Focus) == DrawItemState.Focus ||
                Focused && e.Index == GetFocusedIndex();
            var backColor = isSelected
                ? UiTheme.SelectedBackColor
                : UiTheme.GetChatCategoryBackColor(chatItem);
            var foreColor = isSelected
                ? UiTheme.SelectedTextColor
                : ForeColor;
            using (var backBrush = new SolidBrush(backColor))
            using (var stripeBrush = new SolidBrush(UiTheme.GetChatCategoryColor(chatItem)))
            using (var icon = UiTheme.CreateChatCategoryGlyph(chatItem, 16))
            {
                e.Graphics.FillRectangle(backBrush, e.Bounds);
                e.Graphics.FillRectangle(stripeBrush, e.Bounds.Left, e.Bounds.Top, UiTheme.Scale(4), e.Bounds.Height);

                var iconRect = new Rectangle(
                    e.Bounds.Left + UiTheme.Scale(9),
                    e.Bounds.Top + (e.Bounds.Height - icon.Height) / 2,
                    icon.Width,
                    icon.Height);
                e.Graphics.DrawImage(icon, iconRect);

                var text = BuildRenderText(chatItem != null && chatItem.DisplayText != null ? chatItem.DisplayText : Items[e.Index].ToString());
                var textWidth = Math.Max(0, e.Bounds.Width - UiTheme.Scale(36));
                var measuredSize = TextRenderer.MeasureText(e.Graphics, text, e.Font, new Size(textWidth, int.MaxValue), MessageTextFlags);
                var textHeight = Math.Min(measuredSize.Height, _twoLineTextHeight);
                var textBounds = new Rectangle(
                    e.Bounds.Left + UiTheme.Scale(32),
                    e.Bounds.Top + (e.Bounds.Height - textHeight) / 2,
                    textWidth,
                    textHeight);
                TextRenderer.DrawText(
                    e.Graphics,
                    text,
                    e.Font,
                    textBounds,
                    foreColor,
                    MessageTextFlags);
            }

            if (isFocused)
            {
                e.DrawFocusRectangle();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (ProcessExplorerSelectionKey(e.KeyData))
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                SelectItemForContextMenu(e.Location);
            }

            base.OnMouseDown(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (ProcessExplorerSelectionKey(keyData))
            {
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override bool ProcessKeyMessage(ref Message m)
        {
            if (ProcessKeyMessageCore(ref m, ModifierKeys))
            {
                return true;
            }

            return base.ProcessKeyMessage(ref m);
        }

        private bool ProcessKeyMessageCore(ref Message m, Keys modifiers)
        {
            if ((m.Msg == WmKeyDown || m.Msg == WmSysKeyDown) &&
                ProcessExplorerSelectionKey(((Keys)m.WParam.ToInt32() & Keys.KeyCode) | modifiers))
            {
                return true;
            }

            return false;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (ProcessExplorerSelectionKey(keyData))
            {
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            if (_suppressSelectedIndexChanged)
            {
                return;
            }

            base.OnSelectedIndexChanged(e);

            if (_updatingSelection || SelectedIndex < 0)
            {
                return;
            }

            ClearLogicalSelection();
            _selectionAnchorIndex = SelectedIndex;
            SetFocusedIndexCore(SelectedIndex, false);
        }

        private bool ProcessExplorerSelectionKey(Keys keyData)
        {
            if (Items.Count <= 0)
            {
                return false;
            }

            var keyCode = keyData & Keys.KeyCode;
            var modifiers = keyData & Keys.Modifiers;
            if (modifiers == Keys.None && (keyCode == Keys.Left || keyCode == Keys.Right))
            {
                return RequestAnchorNavigation(keyCode == Keys.Left ? -1 : 1);
            }

            if (modifiers == Keys.None && (keyCode == Keys.Up || keyCode == Keys.Down))
            {
                MoveSelection(keyCode == Keys.Up ? -1 : 1);
                return true;
            }

            if (modifiers == Keys.None && (keyCode == Keys.Home || keyCode == Keys.End))
            {
                MoveSelectionTo(keyCode == Keys.Home ? 0 : Items.Count - 1);
                return true;
            }

            if (modifiers == Keys.Control && (keyCode == Keys.Up || keyCode == Keys.Down))
            {
                MoveFocusedIndex(keyCode == Keys.Up ? -1 : 1);
                return true;
            }

            if (modifiers == Keys.Control && (keyCode == Keys.Home || keyCode == Keys.End))
            {
                MoveFocusedIndexTo(keyCode == Keys.Home ? 0 : Items.Count - 1);
                return true;
            }

            if (modifiers == Keys.Shift && (keyCode == Keys.Up || keyCode == Keys.Down))
            {
                ExtendSelection(keyCode == Keys.Up ? -1 : 1);
                return true;
            }

            if (modifiers == Keys.Shift && (keyCode == Keys.Home || keyCode == Keys.End))
            {
                ExtendSelectionTo(keyCode == Keys.Home ? 0 : Items.Count - 1);
                return true;
            }

            if (modifiers == (Keys.Control | Keys.Shift) && (keyCode == Keys.Home || keyCode == Keys.End))
            {
                ExtendSelectionTo(keyCode == Keys.Home ? 0 : Items.Count - 1);
                return true;
            }

            if ((modifiers == Keys.None || modifiers == Keys.Control) && keyCode == Keys.Space)
            {
                ToggleFocusedSelection();
                return true;
            }

            if (modifiers == Keys.Control && keyCode == Keys.A)
            {
                SelectAllItems();
                return true;
            }

            if (modifiers == Keys.Control && keyCode == Keys.C)
            {
                return CopySelectionToClipboard();
            }

            return false;
        }

        private void MoveSelection(int direction)
        {
            var currentIndex = GetFocusableIndex(direction);
            var targetIndex = ClampIndex(currentIndex + direction);
            if (targetIndex == currentIndex)
            {
                return;
            }

            SelectSingleIndex(targetIndex);
        }

        private void MoveSelectionTo(int targetIndex)
        {
            SelectSingleIndex(ClampIndex(targetIndex));
        }

        private void MoveFocusedIndex(int direction)
        {
            var currentIndex = GetFocusableIndex(direction);
            var targetIndex = ClampIndex(currentIndex + direction);
            if (targetIndex == currentIndex)
            {
                return;
            }

            FocusIndexPreservingSelection(targetIndex);
        }

        private void MoveFocusedIndexTo(int targetIndex)
        {
            var currentIndex = GetFocusableIndex(targetIndex < GetFocusableIndex(1) ? -1 : 1);
            var nextIndex = ClampIndex(targetIndex);
            if (nextIndex == currentIndex)
            {
                return;
            }

            FocusIndexPreservingSelection(nextIndex);
        }

        private void ExtendSelection(int direction)
        {
            var currentIndex = GetFocusableIndex(direction);
            var anchorIndex = IsValidIndex(_selectionAnchorIndex) ? _selectionAnchorIndex : currentIndex;
            var targetIndex = ClampIndex(currentIndex + direction);
            ExtendSelectionRange(anchorIndex, targetIndex);
        }

        private void ExtendSelectionTo(int targetIndex)
        {
            var currentIndex = GetFocusableIndex(targetIndex < GetFocusableIndex(1) ? -1 : 1);
            var anchorIndex = IsValidIndex(_selectionAnchorIndex) ? _selectionAnchorIndex : currentIndex;
            ExtendSelectionRange(anchorIndex, ClampIndex(targetIndex));
        }

        private void ExtendSelectionRange(int anchorIndex, int targetIndex)
        {
            var start = Math.Min(anchorIndex, targetIndex);
            var end = Math.Max(anchorIndex, targetIndex);

            BeginUpdate();
            _updatingSelection = true;
            try
            {
                SetLogicalRange(start, end);
                SelectNativeFocusIndex(targetIndex);
                _selectionAnchorIndex = anchorIndex;
                SetFocusedIndexCore(targetIndex, true);
            }
            finally
            {
                _updatingSelection = false;
                EndUpdate();
            }

            RaiseSelectionSummary(start, end, targetIndex, start == 0 && end == Items.Count - 1);
        }

        private void ToggleFocusedSelection()
        {
            var index = GetFocusableIndex(1);
            if (!IsValidIndex(index))
            {
                return;
            }

            bool selected;
            _updatingSelection = true;
            try
            {
                EnsureLogicalSelectionInitialized();
                selected = !_logicalSelectedIndices.Contains(index);
                if (selected)
                {
                    _logicalSelectedIndices.Add(index);
                }
                else
                {
                    _logicalSelectedIndices.Remove(index);
                }

                SelectNativeFocusIndex(index);
            }
            finally
            {
                _updatingSelection = false;
            }

            _selectionAnchorIndex = index;
            SetFocusedIndexCore(index, true);
        }

        private void SelectAllItems()
        {
            if (Items.Count <= 0)
            {
                return;
            }

            BeginUpdate();
            _updatingSelection = true;
            var focusedIndex = GetFocusableIndex(1);
            try
            {
                SetLogicalRange(0, Items.Count - 1);
                SelectNativeFocusIndex(focusedIndex);
                _selectionAnchorIndex = focusedIndex;
                SetFocusedIndexCore(focusedIndex, true);
            }
            finally
            {
                _updatingSelection = false;
                EndUpdate();
            }

            RaiseSelectionSummary(0, Items.Count - 1, focusedIndex, true);
        }

        private bool CanCopySelection()
        {
            return !string.IsNullOrWhiteSpace(BuildClipboardTextForSelection());
        }

        private void SelectItemForContextMenu(Point location)
        {
            var index = IndexFromPoint(location);
            if (!IsValidIndex(index))
            {
                return;
            }

            if (!IsIndexSelectedForDisplay(index, DrawItemState.None))
            {
                SelectSingleIndex(index);
                return;
            }

            SetFocusedIndexCore(index, true);
        }

        private bool IsIndexSelectedForDisplay(int index, DrawItemState state)
        {
            if (_isLogicalSelectionActive)
            {
                return _logicalSelectedIndices.Contains(index);
            }

            return (state & DrawItemState.Selected) == DrawItemState.Selected ||
                   IsValidIndex(index) && GetSelected(index);
        }

        private bool RequestAnchorNavigation(int direction)
        {
            var handler = AnchorNavigationRequested;
            if (handler == null)
            {
                return false;
            }

            var args = new ChatHistoryAnchorNavigationEventArgs(direction);
            handler(this, args);
            return args.Handled;
        }

        private void EnsureLogicalSelectionInitialized()
        {
            if (_isLogicalSelectionActive)
            {
                return;
            }

            _isLogicalSelectionActive = true;
            foreach (int index in SelectedIndices)
            {
                if (IsValidIndex(index))
                {
                    _logicalSelectedIndices.Add(index);
                }
            }
        }

        private void SetLogicalRange(int start, int end)
        {
            _isLogicalSelectionActive = true;
            _logicalSelectedIndices.Clear();
            for (var index = start; index <= end; index++)
            {
                if (IsValidIndex(index))
                {
                    _logicalSelectedIndices.Add(index);
                }
            }
        }

        private void ClearLogicalSelection()
        {
            _isLogicalSelectionActive = false;
            _logicalSelectedIndices.Clear();
        }

        private void SelectNativeFocusIndex(int index)
        {
            if (!IsValidIndex(index))
            {
                return;
            }

            ClearSelectedSilently();
            SetSelected(index, true);
        }

        private void ClearSelectedSilently()
        {
            _suppressSelectedIndexChanged = true;
            try
            {
                ClearSelected();
            }
            finally
            {
                _suppressSelectedIndexChanged = false;
            }
        }

        private void FocusIndexPreservingSelection(int index)
        {
            if (!IsValidIndex(index))
            {
                return;
            }

            BeginUpdate();
            _updatingSelection = true;
            var previousIndex = _focusedIndex;
            try
            {
                EnsureLogicalSelectionInitialized();
                _focusedIndex = index;
                SelectNativeFocusIndex(index);
                _selectionAnchorIndex = index;
                EnsureIndexVisible(index);

                if (IsHandleCreated)
                {
                    SendMessage(Handle, LbSetCaretIndex, new IntPtr(index), new IntPtr(1));
                }

                InvalidateItem(previousIndex);
                InvalidateItem(index);
            }
            finally
            {
                _updatingSelection = false;
                EndUpdate();
            }
        }

        private void RaiseSelectionSummary(int startIndex, int endIndex, int focusedIndex, bool isAllSelected)
        {
            var selectedCount = GetSelectedIndicesInDisplayOrder().Length;
            if (selectedCount <= 0)
            {
                return;
            }

            var summaryText = isAllSelected
                ? string.Format(
                    UiLocalization.Text("ChatHistoryListBox.Selection.AllSelected.Template", "Selected all {0} messages."),
                    selectedCount)
                : string.Format(
                    UiLocalization.Text("ChatHistoryListBox.Selection.SelectedCount.Template", "Selected {0} messages."),
                    selectedCount);

            SelectionSummaryRequested?.Invoke(
                this,
                new ChatHistorySelectionSummaryEventArgs(
                    selectedCount,
                    Items.Count,
                    startIndex,
                    endIndex,
                    focusedIndex,
                    isAllSelected,
                    summaryText));
        }

        private int GetFocusableIndex(int direction)
        {
            var focusedIndex = GetFocusedIndex();
            if (focusedIndex >= 0)
            {
                return focusedIndex;
            }

            if (SelectedIndex >= 0)
            {
                return SelectedIndex;
            }

            return direction < 0 ? Items.Count - 1 : 0;
        }

        private int GetFocusedIndex()
        {
            if (IsValidIndex(_focusedIndex))
            {
                return _focusedIndex;
            }

            return SelectedIndex >= 0 && SelectedIndex < Items.Count ? SelectedIndex : -1;
        }

        private void SetFocusedIndexCore(int index, bool ensureVisible)
        {
            if (!IsValidIndex(index))
            {
                return;
            }

            var previousIndex = _focusedIndex;
            _focusedIndex = index;
            if (ensureVisible)
            {
                EnsureIndexVisible(index);
            }

            if (IsHandleCreated)
            {
                SendMessage(Handle, LbSetCaretIndex, new IntPtr(index), ensureVisible ? new IntPtr(1) : IntPtr.Zero);
            }

            InvalidateItem(previousIndex);
            InvalidateItem(index);
        }

        private void EnsureIndexVisible(int index)
        {
            if (!IsValidIndex(index))
            {
                return;
            }

            if (index < TopIndex)
            {
                TopIndex = index;
                return;
            }

            var visibleCount = Math.Max(1, ClientSize.Height / Math.Max(1, ItemHeight));
            if (index >= TopIndex + visibleCount)
            {
                TopIndex = Math.Max(0, index - visibleCount + 1);
            }
        }

        private void InvalidateItem(int index)
        {
            if (!IsValidIndex(index))
            {
                return;
            }

            try
            {
                Invalidate(GetItemRectangle(index));
            }
            catch
            {
            }
        }

        private int ClampIndex(int index)
        {
            if (Items.Count <= 0)
            {
                return -1;
            }

            if (index < 0)
            {
                return 0;
            }

            return index >= Items.Count ? Items.Count - 1 : index;
        }

        private bool IsValidIndex(int index)
        {
            return index >= 0 && index < Items.Count;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
