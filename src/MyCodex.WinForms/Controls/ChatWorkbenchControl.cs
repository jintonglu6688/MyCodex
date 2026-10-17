using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Controls
{
    internal sealed class ChatWorkbenchControl : UserControl
    {
        private readonly ChatWorkbenchHostCallbacks _callbacks;
        private readonly List<PendingAttachmentItem> _pendingAttachments;
        private readonly ChatHistoryListBox _historyListBox;
        private readonly AttachmentComposerTextBox _inputTextBox;
        private readonly ListBox _pendingAttachmentListBox;
        private readonly TextBox _statusTextBox;
        private readonly Panel _composePanel;
        private readonly Panel _attachmentPanel;
        private readonly Button _insertButton;
        private readonly Button _sendButton;
        private readonly Button _stopButton;
        private readonly ContextMenu _attachmentMenu;
        private readonly MenuItem _selectImageAttachmentMenuItem;
        private readonly MenuItem _selectFileAttachmentMenuItem;
        private readonly MenuItem _selectFolderAttachmentMenuItem;
        private readonly MenuItem _selectSkillAttachmentMenuItem;
        private readonly MenuItem _selectPluginAttachmentMenuItem;
        private readonly ContextMenu _pendingAttachmentContextMenu;
        private readonly MenuItem _removeSelectedAttachmentMenuItem;
        private readonly MenuItem _clearAttachmentsMenuItem;
        private bool _isSending;
        private bool _isUpdatingInputText;

        public ChatWorkbenchControl(ChatWorkbenchHostCallbacks callbacks)
        {
            _callbacks = callbacks ?? new ChatWorkbenchHostCallbacks();
            _pendingAttachments = new List<PendingAttachmentItem>();

            Dock = DockStyle.Fill;
            BackColor = UiTheme.WindowBackColor;
            TabStop = false;

            _historyListBox = CreateHistoryListBox();
            _inputTextBox = CreateInputBox();
            _pendingAttachmentListBox = CreatePendingAttachmentListBox();
            _statusTextBox = CreateStatusTextBox();
            _insertButton = CreateButton(
                UiLocalization.Text("MainForm.Button.Attach", "&Attach"),
                UiLocalization.Text("ChatWorkbenchControl.Accessible.Insert", "Insert"),
                2,
                UiGlyph.Attach);
            _sendButton = CreateButton(
                UiLocalization.Text("MainForm.Button.Send", "&Send"),
                UiLocalization.Text("ChatWorkbenchControl.Accessible.Send", "Send"),
                4,
                UiGlyph.Send);
            _stopButton = CreateButton(
                UiLocalization.Text("MainForm.Button.Stop", "&Stop"),
                UiLocalization.Text("ChatWorkbenchControl.Accessible.Stop", "Stop"),
                5,
                UiGlyph.Stop);
            _pendingAttachmentContextMenu = new ContextMenu();
            _attachmentMenu = new ContextMenu();
            _selectImageAttachmentMenuItem = new MenuItem(UiLocalization.Text("ChatWorkbenchControl.Attachment.Image", "Image(&I)..."));
            _selectFileAttachmentMenuItem = new MenuItem(UiLocalization.Text("ChatWorkbenchControl.Attachment.File", "File(&F)..."));
            _selectFolderAttachmentMenuItem = new MenuItem(UiLocalization.Text("ChatWorkbenchControl.Attachment.Folder", "Folder(&D)..."));
            _selectSkillAttachmentMenuItem = new MenuItem(UiLocalization.Text("ChatWorkbenchControl.Attachment.Skill", "Skill(&K)..."));
            _selectPluginAttachmentMenuItem = new MenuItem(UiLocalization.Text("MainForm.Attachment.Plugin", "Plugin(&P)..."));
            _removeSelectedAttachmentMenuItem = new MenuItem(UiLocalization.Text("ChatWorkbenchControl.Attachment.RemoveSelected", "Remove Selected(&R)"));
            _clearAttachmentsMenuItem = new MenuItem(UiLocalization.Text("ChatWorkbenchControl.Attachment.ClearItems", "Clear Items(&C)"));

            _insertButton.Click += async (sender, e) => await SelectAndAddAttachmentsAsync();
            _selectImageAttachmentMenuItem.Click += async (sender, e) => await SelectAndAddAttachmentsAsync(_callbacks.SelectImageAttachmentsAsync);
            _selectFileAttachmentMenuItem.Click += async (sender, e) => await SelectAndAddAttachmentsAsync(_callbacks.SelectFileAttachmentsAsync);
            _selectFolderAttachmentMenuItem.Click += async (sender, e) => await SelectAndAddAttachmentsAsync(_callbacks.SelectFolderAttachmentsAsync);
            _selectSkillAttachmentMenuItem.Click += async (sender, e) => await SelectAndAddAttachmentsAsync(_callbacks.SelectSkillAttachmentsAsync);
            _selectPluginAttachmentMenuItem.Click += async (sender, e) => await SelectAndAddAttachmentsAsync(_callbacks.SelectPluginAttachmentsAsync);
            _sendButton.Click += async (sender, e) => await SendCurrentInputAsync();
            _stopButton.Click += async (sender, e) => await StopAsync();
            _inputTextBox.KeyDown += InputTextBox_KeyDown;
            _historyListBox.KeyDown += HistoryListBox_KeyDown;
            _historyListBox.SelectionSummaryRequested += HistoryListBox_SelectionSummaryRequested;
            _inputTextBox.TextChanged += InputTextBox_TextChanged;
            _pendingAttachmentListBox.MouseDown += PendingAttachmentListBox_MouseDown;
            _pendingAttachmentListBox.KeyDown += PendingAttachmentListBox_KeyDown;
            _pendingAttachmentContextMenu.Popup += PendingAttachmentContextMenu_Popup;
            _attachmentMenu.MenuItems.Add(_selectImageAttachmentMenuItem);
            _attachmentMenu.MenuItems.Add(_selectFileAttachmentMenuItem);
            _attachmentMenu.MenuItems.Add(_selectFolderAttachmentMenuItem);
            _attachmentMenu.MenuItems.Add(_selectSkillAttachmentMenuItem);
            _attachmentMenu.MenuItems.Add(_selectPluginAttachmentMenuItem);
            _removeSelectedAttachmentMenuItem.Click += delegate { RemoveSelectedPendingAttachment(); };
            _clearAttachmentsMenuItem.Click += delegate { RemoveAllAttachmentMarkersFromInput(); ClearPendingAttachments(true); };
            _pendingAttachmentContextMenu.MenuItems.Add(_removeSelectedAttachmentMenuItem);
            _pendingAttachmentContextMenu.MenuItems.Add(_clearAttachmentsMenuItem);
            _pendingAttachmentListBox.ContextMenu = _pendingAttachmentContextMenu;

            _attachmentPanel = BuildAttachmentPanel();
            _composePanel = BuildComposePanel();
            Controls.Add(_historyListBox);
            Controls.Add(_composePanel);
            Controls.Add(_statusTextBox);
            RefreshPendingAttachmentState();
            RefreshActionState();
        }

        internal string InputText
        {
            get { return _inputTextBox.ComposeText; }
        }

        internal IReadOnlyList<PendingAttachmentItem> PendingAttachments
        {
            get { return _pendingAttachments.ToArray(); }
        }

        internal bool HasAttachmentActions
        {
            get { return _callbacks.SelectAttachmentsAsync != null || HasSpecificAttachmentCallbacks(); }
        }

        internal async Task<int> PasteAttachmentsFromHostAsync()
        {
            if (_callbacks.CanPasteAttachments == null ||
                !_callbacks.CanPasteAttachments() ||
                _callbacks.PasteAttachmentsAsync == null)
            {
                return 0;
            }

            return await SelectAndAddAttachmentsAsync(_callbacks.PasteAttachmentsAsync);
        }

        internal void SetInputText(string text)
        {
            SetInputText(text, (text ?? string.Empty).Length, 0);
        }

        internal void ReplaceHistoryItems(IEnumerable<ChatHistoryItem> items)
        {
            var topIndex = _historyListBox.TopIndex;
            var replacementItems = (items ?? Array.Empty<ChatHistoryItem>())
                .Where(item => item != null)
                .ToArray();
            if (!ChatHistoryVisibleItemsUpdater.HasChanges(_historyListBox.Items, replacementItems)) return;

            var selectionSnapshot = ChatHistorySelectionSnapshot.Capture(
                _historyListBox.PrimaryChatHistoryItem,
                _historyListBox.FocusedIndex);
            _historyListBox.BeginUpdate();
            try
            {
                if (!ChatHistoryVisibleItemsUpdater.Apply(_historyListBox.Items, replacementItems)) return;

                var selectedIndex = selectionSnapshot != null
                    ? selectionSnapshot.ResolveSelectedIndex(replacementItems)
                    : -1;
                if (selectedIndex >= 0 && _historyListBox.FocusedIndex != selectedIndex)
                {
                    _historyListBox.SelectSingleIndex(selectedIndex);
                }
                if (topIndex >= 0 && _historyListBox.Items.Count > 0)
                    _historyListBox.TopIndex = Math.Min(topIndex, _historyListBox.Items.Count - 1);
            }
            finally
            {
                _historyListBox.EndUpdate();
            }
        }

        internal ChatHistoryItem[] GetHistoryItems()
        {
            return _historyListBox.Items.OfType<ChatHistoryItem>().ToArray();
        }

        internal ChatHistorySelectionSnapshot CaptureHistorySelection()
        {
            return ChatHistorySelectionSnapshot.Capture(
                _historyListBox.PrimaryChatHistoryItem,
                _historyListBox.FocusedIndex);
        }

        internal bool RestoreHistorySelection(ChatHistorySelectionSnapshot snapshot)
        {
            var index = snapshot != null
                ? snapshot.ResolveMatchingIndex(GetHistoryItems())
                : -1;
            if (index < 0)
            {
                return false;
            }

            _historyListBox.SelectSingleIndex(index);
            EnsureHistoryItemVisible(index);
            return true;
        }

        internal void EnsureHistoryItemVisible(int index)
        {
            if (index < 0 || index >= _historyListBox.Items.Count)
            {
                return;
            }

            try
            {
                _historyListBox.TopIndex = index;
            }
            catch
            {
            }
        }

        internal void AddHistoryItem(ChatHistoryItem item)
        {
            if (item == null)
            {
                return;
            }

            _historyListBox.Items.Add(item);
            if (_historyListBox.Items.Count > 0)
            {
                _historyListBox.TopIndex = _historyListBox.Items.Count - 1;
            }
        }

        internal void SetStatus(string text)
        {
            if (string.Equals(_statusTextBox.Text, text ?? string.Empty, StringComparison.Ordinal)) return;
            _statusTextBox.Text = text ?? string.Empty;
            if (_callbacks.AnnounceStatus != null && !string.IsNullOrWhiteSpace(text))
            {
                _callbacks.AnnounceStatus(text);
            }
        }

        internal void SetSendingState(bool isSending)
        {
            _isSending = isSending;
            RefreshActionState();
        }

        internal bool FocusMessages()
        {
            return !_historyListBox.IsDisposed && _historyListBox.CanFocus && _historyListBox.Focus();
        }

        internal bool FocusInput()
        {
            return !_inputTextBox.IsDisposed && _inputTextBox.CanFocus && _inputTextBox.Focus();
        }

        internal bool FocusLastInteractiveControl()
        {
            if (!_stopButton.IsDisposed && _stopButton.CanFocus && _stopButton.Focus())
            {
                return true;
            }

            if (!_sendButton.IsDisposed && _sendButton.CanFocus && _sendButton.Focus())
            {
                return true;
            }

            if (!_insertButton.IsDisposed && _insertButton.CanFocus && _insertButton.Focus())
            {
                return true;
            }

            return !_inputTextBox.IsDisposed && _inputTextBox.CanFocus && _inputTextBox.Focus();
        }

        internal int AddPendingAttachments(IReadOnlyCollection<PendingAttachmentItem> attachments)
        {
            var added = new List<PendingAttachmentItem>();
            foreach (var attachment in attachments ?? Array.Empty<PendingAttachmentItem>())
            {
                if (attachment == null || ContainsPendingAttachment(attachment.LocalPath))
                {
                    continue;
                }

                var markerText = !string.IsNullOrWhiteSpace(attachment.MarkerText)
                    ? attachment.MarkerText
                    : ComposeAttachmentState.BuildAttachmentMarkerText(_pendingAttachments, attachment.Kind, attachment.DisplayName, attachment.LocalPath, attachment.IsDirectory);
                var normalized = new PendingAttachmentItem(
                    attachment.LocalPath,
                    attachment.DisplayName,
                    attachment.IsTemporaryFile,
                    attachment.Kind,
                    markerText,
                    attachment.InMemoryContent,
                    attachment.InputName,
                    attachment.IsDirectory,
                    attachment.IsRemotePath);
                _pendingAttachments.Add(normalized);
                added.Add(normalized);
            }

            if (added.Count == 0)
            {
                return 0;
            }

            InsertAttachmentMarkersAtCursor(added);
            RefreshPendingAttachmentState();
            return added.Count;
        }

        internal ComposeDraftState CaptureDraftState()
        {
            return ComposeAttachmentState.CaptureDraftState(InputText, _pendingAttachments);
        }

        internal void RestoreDraftState(ComposeDraftState state)
        {
            ClearPendingAttachments(false);
            if (state == null)
            {
                SetInputText(string.Empty);
                return;
            }

            foreach (var attachment in ComposeAttachmentState.CloneAttachments(state.Attachments))
            {
                _pendingAttachments.Add(attachment);
            }

            SetInputText(state.DraftText);
            SynchronizePendingAttachmentsWithInputText(false);
            RefreshPendingAttachmentState();
        }

        internal async Task SendCurrentInputAsync()
        {
            if (_callbacks.SendAsync == null)
            {
                return;
            }

            SynchronizePendingAttachmentsWithInputText(true);
            var rawText = InputText;
            var attachmentsSnapshot = ComposeAttachmentState.CloneAttachments(_pendingAttachments);
            var inputItems = BuildOutgoingInputItems(rawText, attachmentsSnapshot);
            if (inputItems.Count == 0)
            {
                return;
            }

            var unavailableAttachments = _callbacks.ValidateAttachmentsAsync != null
                ? await _callbacks.ValidateAttachmentsAsync(attachmentsSnapshot)
                : Array.Empty<PendingAttachmentItem>();
            if (unavailableAttachments != null && unavailableAttachments.Count > 0)
            {
                SetStatus(UiLocalization.Text("ChatWorkbenchControl.Status.AttachmentsUnavailable", "Attachments are unavailable. Please add them again"));
                return;
            }

            var request = new ChatWorkbenchSendRequest(
                rawText,
                OutgoingInputItemBuilder.BuildPreviewText(rawText, attachmentsSnapshot),
                inputItems,
                attachmentsSnapshot);

            _isSending = true;
            RefreshActionState();
            SetStatus(UiLocalization.Text("ChatWorkbenchControl.Status.Sending", "Sending message..."));
            try
            {
                var result = await _callbacks.SendAsync(request);
                if (result == null || !result.KeepSending)
                {
                    _isSending = false;
                }

                if (result != null && result.ClearInput)
                {
                    SetInputText(string.Empty);
                    ClearPendingAttachments(false);
                }

                if (result != null && !string.IsNullOrWhiteSpace(result.StatusText))
                {
                    SetStatus(result.StatusText);
                }
            }
            catch (Exception ex)
            {
                _isSending = false;
                SetStatus(UiLocalization.Text("ChatWorkbenchControl.Status.SendFailed", "Send failed"));
                AddHistoryItem(CreateHistoryItem(UiLocalization.Text("MainForm.Chat.Sender.Error", "Error"), ex.Message, ChatHistoryCategory.Error));
            }
            finally
            {
                RefreshActionState();
            }
        }

        private List<AppServerUserInputItem> BuildOutgoingInputItems(string text, IReadOnlyList<PendingAttachmentItem> attachments)
        {
            return OutgoingInputItemBuilder.Build(text, attachments, _callbacks.ReadImageBytes);
        }

        private async Task SelectAndAddAttachmentsAsync()
        {
            if (HasSpecificAttachmentCallbacks())
            {
                ShowAttachmentMenu();
                return;
            }

            await SelectAndAddAttachmentsAsync(_callbacks.SelectAttachmentsAsync);
        }

        private async Task<int> SelectAndAddAttachmentsAsync(Func<Task<IReadOnlyList<PendingAttachmentItem>>> selector)
        {
            if (selector == null)
            {
                return 0;
            }

            var attachments = await selector();
            return AddPendingAttachments((attachments ?? Array.Empty<PendingAttachmentItem>()).ToArray());
        }

        private bool HasSpecificAttachmentCallbacks()
        {
            return _callbacks.SelectImageAttachmentsAsync != null ||
                   _callbacks.SelectFileAttachmentsAsync != null ||
                   _callbacks.SelectFolderAttachmentsAsync != null ||
                   _callbacks.SelectSkillAttachmentsAsync != null ||
                   _callbacks.SelectPluginAttachmentsAsync != null;
        }

        private void ShowAttachmentMenu()
        {
            _selectImageAttachmentMenuItem.Enabled = _callbacks.SelectImageAttachmentsAsync != null;
            _selectFileAttachmentMenuItem.Enabled = _callbacks.SelectFileAttachmentsAsync != null;
            _selectFolderAttachmentMenuItem.Enabled = _callbacks.SelectFolderAttachmentsAsync != null;
            _selectSkillAttachmentMenuItem.Enabled = _callbacks.SelectSkillAttachmentsAsync != null;
            _selectPluginAttachmentMenuItem.Enabled = _callbacks.SelectPluginAttachmentsAsync != null;
            _attachmentMenu.Show(_insertButton, new Point(0, _insertButton.Height));
        }

        private async Task StopAsync()
        {
            if (_callbacks.StopAsync == null)
            {
                return;
            }

            await _callbacks.StopAsync();
            _isSending = false;
            RefreshActionState();
        }

        private void InsertAttachmentMarkersAtCursor(IReadOnlyCollection<PendingAttachmentItem> attachments)
        {
            var normalized = (attachments ?? Array.Empty<PendingAttachmentItem>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.MarkerText))
                .ToArray();
            if (normalized.Length == 0)
            {
                return;
            }

            _inputTextBox.InsertAttachments(normalized);
            ComposeAttachmentState.SortByInputText(_pendingAttachments, InputText);
            _inputTextBox.Focus();
        }

        private void SetInputText(string text, int selectionStart, int selectionLength)
        {
            var normalized = text ?? string.Empty;
            _isUpdatingInputText = true;
            try
            {
                _inputTextBox.SetComposeText(
                    normalized,
                    _pendingAttachments,
                    Math.Max(0, Math.Min(selectionStart, normalized.Length)),
                    Math.Max(0, selectionLength));
            }
            finally
            {
                _isUpdatingInputText = false;
            }

            RefreshActionState();
        }

        private void SynchronizePendingAttachmentsWithInputText(bool deleteTemporaryFiles)
        {
            var syncResult = ComposeAttachmentState.SynchronizeWithInputText(_pendingAttachments, InputText);
            if (!syncResult.Changed)
            {
                return;
            }

            if (deleteTemporaryFiles)
            {
                foreach (var attachment in syncResult.RemovedAttachments)
                {
                    DeleteTemporaryAttachmentFile(attachment);
                }
            }

            RefreshPendingAttachmentState();
        }

        private void RemoveSelectedPendingAttachment()
        {
            var selectedItem = _pendingAttachmentListBox.SelectedItem as PendingAttachmentItem;
            if (selectedItem == null)
            {
                return;
            }

            RemoveAttachmentMarkerFromInput(selectedItem);
            DeleteTemporaryAttachmentFile(selectedItem);
            _pendingAttachments.Remove(selectedItem);
            RefreshPendingAttachmentState();
        }

        private void RemoveAllAttachmentMarkersFromInput()
        {
            _inputTextBox.RemoveAllAttachments();
        }

        private void RemoveAttachmentMarkerFromInput(PendingAttachmentItem attachment)
        {
            if (attachment == null || string.IsNullOrWhiteSpace(attachment.MarkerText))
            {
                return;
            }

            _inputTextBox.RemoveAttachment(attachment);
        }

        private void ClearPendingAttachments(bool deleteTemporaryFiles)
        {
            if (deleteTemporaryFiles)
            {
                foreach (var attachment in _pendingAttachments)
                {
                    DeleteTemporaryAttachmentFile(attachment);
                }
            }

            _pendingAttachments.Clear();
            RefreshPendingAttachmentState();
        }

        private void DeleteTemporaryAttachmentFile(PendingAttachmentItem attachment)
        {
            if (attachment == null || !attachment.IsTemporaryFile || _callbacks.DeleteTemporaryAttachment == null)
            {
                return;
            }

            _callbacks.DeleteTemporaryAttachment(attachment);
        }

        private bool ContainsPendingAttachment(string path)
        {
            return ComposeAttachmentState.ContainsPendingAttachment(_pendingAttachments, path);
        }

        private void RefreshPendingAttachmentState()
        {
            _pendingAttachmentListBox.BeginUpdate();
            try
            {
                _pendingAttachmentListBox.Items.Clear();
                foreach (var attachment in _pendingAttachments)
                {
                    _pendingAttachmentListBox.Items.Add(attachment);
                }
            }
            finally
            {
                _pendingAttachmentListBox.EndUpdate();
            }

            var hasAttachments = _pendingAttachments.Count > 0;
            _attachmentPanel.Visible = hasAttachments;
            _composePanel.Height = _composePanel.IsHandleCreated ? UiTheme.Scale(hasAttachments ? 206 : 110) : hasAttachments ? 206 : 110;
            _pendingAttachmentListBox.Enabled = hasAttachments;
            _removeSelectedAttachmentMenuItem.Enabled = hasAttachments && _pendingAttachmentListBox.SelectedItem is PendingAttachmentItem;
            _clearAttachmentsMenuItem.Enabled = hasAttachments;
            RefreshActionState();
        }

        internal void RefreshActionState()
        {
            var hasDraft = !string.IsNullOrWhiteSpace(InputText) || _pendingAttachments.Count > 0;
            var canSend = _callbacks.CanSend == null || _callbacks.CanSend();
            _sendButton.Enabled = canSend && hasDraft && !_isSending;
            _stopButton.Enabled = _isSending || (_callbacks.CanStop != null && _callbacks.CanStop());
            _insertButton.Enabled = canSend && HasAttachmentActions;
        }

        private Panel BuildComposePanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 110,
                BackColor = UiTheme.PanelBackColor,
                Padding = new Padding(0),
                TabStop = false
            };

            var inputPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PanelBackColor,
                TabStop = false
            };
            var inputTitle = CreateSubSectionTitle(UiLocalization.Text("MainForm.Label.Input", "Input(&I)"));
            inputTitle.AccessibleName = UiLocalization.Text("MainForm.Accessible.Input", "Input");
            _inputTextBox.Dock = DockStyle.Fill;
            inputPanel.Controls.Add(_inputTextBox);
            inputPanel.Controls.Add(inputTitle);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = UiTheme.PanelBackColor,
                Padding = new Padding(UiTheme.ControlSpacing, 0, 0, 0),
                TabStop = false
            };
            buttonPanel.Controls.Add(_insertButton);
            buttonPanel.Controls.Add(_sendButton);
            buttonPanel.Controls.Add(_stopButton);

            panel.Controls.Add(_attachmentPanel);
            panel.Controls.Add(inputPanel);
            panel.Controls.Add(buttonPanel);
            return panel;
        }

        private Panel BuildAttachmentPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = UiTheme.AttachmentPanelHeight,
                BackColor = UiTheme.PanelBackColor,
                TabStop = false
            };
            var title = CreateSubSectionTitle(UiLocalization.Text("ChatWorkbenchControl.Label.PendingItems", "Pending Items"));
            _pendingAttachmentListBox.Dock = DockStyle.Fill;
            panel.Controls.Add(_pendingAttachmentListBox);
            panel.Controls.Add(title);
            return panel;
        }

        private static ChatHistoryListBox CreateHistoryListBox()
        {
            var listBox = new ChatHistoryListBox
            {
                AccessibleName = UiLocalization.Text("ChatWorkbenchControl.Accessible.Messages", "Messages"),
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                IntegralHeight = false,
                TabIndex = 0
            };
            UiTheme.ApplyListBox(listBox);
            return listBox;
        }

        private static AttachmentComposerTextBox CreateInputBox()
        {
            var textBox = new AttachmentComposerTextBox
            {
                AccessibleName = UiLocalization.Text("ChatWorkbenchControl.Accessible.Input", "Input"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                TabIndex = 1
            };
            textBox.ApplyTheme();
            return textBox;
        }

        private static ListBox CreatePendingAttachmentListBox()
        {
            var listBox = new ListBox
            {
                AccessibleName = UiLocalization.Text("ChatWorkbenchControl.Accessible.PendingItems", "Pending items"),
                BorderStyle = BorderStyle.FixedSingle,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                IntegralHeight = false,
                TabIndex = 3
            };
            UiTheme.ApplyListBox(listBox);
            return listBox;
        }

        private static TextBox CreateStatusTextBox()
        {
            var textBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("ChatWorkbenchControl.Accessible.Status", "Status"),
                Dock = DockStyle.Bottom,
                Height = 28,
                ReadOnly = true,
                TabStop = false
            };
            UiTheme.ApplyTextBox(textBox, false, true);
            return textBox;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex, UiGlyph glyph)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 88,
                Height = 32,
                TabIndex = tabIndex,
                UseMnemonic = true
            };
            UiTheme.ApplyButton(button, glyph);
            return button;
        }

        private static Label CreateSubSectionTitle(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(0),
                Margin = new Padding(0),
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Bold),
                ForeColor = UiTheme.MutedTextColor,
                BackColor = UiTheme.PanelBackColor,
                TabStop = false
            };
        }

        private static ChatHistoryItem CreateHistoryItem(string sender, string message, ChatHistoryCategory category)
        {
            var prefix = sender + UiLocalization.Text("MainForm.Chat.Prefix.Separator", ":");
            var normalized = message ?? string.Empty;
            var shouldSanitize = ChatMessageTextSanitizer.ShouldSanitizeSender(sender);
            var displayMessage = shouldSanitize
                ? ChatMessageTextSanitizer.SanitizeForDisplay(normalized)
                : normalized.Replace("\r", " ").Replace("\n", " ");
            var accessibleMessage = shouldSanitize
                ? ChatMessageTextSanitizer.SanitizeForReadableText(normalized)
                : normalized;
            var displayText = string.Format("{0} {1}", prefix, displayMessage).Trim();
            var fullText = string.Format("{0} {1}", prefix, normalized).Trim();
            var accessibleText = string.Format("{0} {1}", prefix, accessibleMessage).Trim();
            return new ChatHistoryItem
            {
                Sender = sender,
                Category = category,
                DisplayText = displayText,
                FullText = fullText,
                AccessibleText = shouldSanitize ? accessibleText : fullText,
                MarkdownText = normalized
            };
        }

        private async void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V && _callbacks.CanPasteAttachments != null && _callbacks.CanPasteAttachments())
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                await PasteAttachmentsFromHostAsync();
                return;
            }

            var action = SendMessageShortcutPlanner.ResolveAction(GetSendMessageShortcutMode(), e.KeyCode, e.Control);
            if (action == SendMessageShortcutAction.None || action == SendMessageShortcutAction.NewLine && !e.Control)
            {
                return;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;
            if (action == SendMessageShortcutAction.NewLine)
            {
                InsertInputNewLine();
                return;
            }

            await SendCurrentInputAsync();
        }

        private string GetSendMessageShortcutMode()
        {
            return _callbacks.GetSendMessageShortcutMode != null
                ? _callbacks.GetSendMessageShortcutMode()
                : SendMessageShortcutMode.EnterNewLineCtrlEnterSend;
        }

        private void InsertInputNewLine()
        {
            _inputTextBox.InsertNewLine();
            RefreshActionState();
        }

        private void InputTextBox_TextChanged(object sender, EventArgs e)
        {
            if (!_isUpdatingInputText)
            {
                SynchronizePendingAttachmentsWithInputText(true);
            }

            RefreshActionState();
        }

        private void HistoryListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.KeyData != Keys.Enter && e.KeyData != (Keys.Alt | Keys.Enter)) ||
                _callbacks.OpenMessageDetail == null)
            {
                return;
            }

            var item = _historyListBox.PrimaryChatHistoryItem;
            if (item == null)
            {
                return;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;
            _callbacks.OpenMessageDetail(item);
        }

        private void HistoryListBox_SelectionSummaryRequested(object sender, ChatHistorySelectionSummaryEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.SummaryText))
            {
                return;
            }

            SetStatus(e.SummaryText);
        }

        private void PendingAttachmentListBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var index = _pendingAttachmentListBox.IndexFromPoint(e.Location);
            if (index >= 0)
            {
                _pendingAttachmentListBox.SelectedIndex = index;
                return;
            }

            _pendingAttachmentListBox.ClearSelected();
        }

        private void PendingAttachmentListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelectedPendingAttachment();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Apps && _pendingAttachmentListBox.SelectedIndex >= 0)
            {
                var rect = _pendingAttachmentListBox.GetItemRectangle(_pendingAttachmentListBox.SelectedIndex);
                _pendingAttachmentContextMenu.Show(_pendingAttachmentListBox, new Point(rect.Left + 8, rect.Bottom));
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void PendingAttachmentContextMenu_Popup(object sender, EventArgs e)
        {
            var hasItems = _pendingAttachments.Count > 0;
            _removeSelectedAttachmentMenuItem.Enabled = hasItems && _pendingAttachmentListBox.SelectedItem is PendingAttachmentItem;
            _clearAttachmentsMenuItem.Enabled = hasItems;
        }
    }
}
