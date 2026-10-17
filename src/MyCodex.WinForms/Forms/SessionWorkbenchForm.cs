using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Helpers;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class SessionWorkbenchForm : Form
    {
        private readonly SessionWorkbenchContext _context;
        private readonly SessionWorkbenchOperations _operations;
        private readonly ChatWorkbenchControl _chatWorkbench;
        private readonly ActivityVirtualBrowseControl _activityBrowseControl;
        private readonly Label _activityCurrentStatusLabel;
        private readonly Button _fileBrowserButton;
        private readonly Button _gitWorkbenchButton;
        private readonly Button _terminalButton;
        private readonly Button _subAgentsButton;
        private readonly Dictionary<string, TurnProgressSummaryState> _turnSummaryStates;
        private readonly Dictionary<string, ActivityTimelineState> _activityTimelineStates;
        private bool _isLoadingHistory;
        private bool _isSending;
        private bool _isClosingForSessionRemoval;
        private bool _isClosingForTargetExit;
        private int _pendingSends;
        private ChatHistorySelectionSnapshot _pendingMessageSelectionRestore;

        internal SessionWorkbenchForm(SessionWorkbenchContext context, SessionWorkbenchOperations operations)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _operations = operations ?? throw new ArgumentNullException(nameof(operations));
            _turnSummaryStates = new Dictionary<string, TurnProgressSummaryState>(StringComparer.OrdinalIgnoreCase);
            _activityTimelineStates = new Dictionary<string, ActivityTimelineState>(StringComparer.OrdinalIgnoreCase);
            _isSending = _operations.IsWorking != null && _operations.IsWorking(_context.Session);

            Text = SessionWorkbenchTitleComposer.Build(_context.Workspace, _context.Session, IsBusy);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 600);
            Size = new Size(1040, 740);
            ShowInTaskbar = true;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _chatWorkbench = new ChatWorkbenchControl(CreateWorkbenchCallbacks());
            _activityBrowseControl = new ActivityVirtualBrowseControl
            {
                AccessibleName = UiLocalization.Text("MainForm.Accessible.Activity", "Activity"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                TabIndex = 10
            };
            _activityCurrentStatusLabel = new Label
            {
                AccessibleName = UiLocalization.Text("MainForm.Accessible.CurrentActivity", "Current activity"),
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                Padding = new Padding(4, 0, 0, 0),
                TabStop = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _fileBrowserButton = CreateToolButton(
                UiLocalization.Text("MainForm.Workspace.AppServerFiles", "File Browser(&F)..."),
                UiLocalization.Text("MainForm.Accessible.FileBrowser", "File browser"),
                6,
                UiGlyph.Workspace);
            _gitWorkbenchButton = CreateToolButton(
                UiLocalization.Text("MainForm.Button.DiffWorkbench", "Git Workbench(&H)"),
                UiLocalization.Text("MainForm.Accessible.GitWorkbench", "Git workbench"),
                7,
                UiGlyph.Model);
            _terminalButton = CreateToolButton(
                UiLocalization.Text("MainForm.Button.Command", "Terminal(&K)"),
                UiLocalization.Text("MainForm.Accessible.Command", "Terminal"),
                8,
                UiGlyph.Command);
            _subAgentsButton = CreateToolButton(
                UiLocalization.Text("MainForm.Session.SubAgents", "View Suba&gents..."),
                UiLocalization.Text("SubAgentWorkbenchForm.Accessible.Subagents", "Subagents"),
                9,
                UiGlyph.User);

            _fileBrowserButton.Click += delegate { _operations.OpenFileBrowser?.Invoke(); };
            _gitWorkbenchButton.Click += delegate { _operations.OpenGitWorkbench?.Invoke(); };
            _terminalButton.Click += delegate { _operations.OpenTerminal?.Invoke(); };
            _subAgentsButton.Click += delegate { _operations.OpenSubAgents?.Invoke(); };
            Shown += async (sender, e) => await InitializeAsync();
            FormClosing += SessionWorkbenchForm_FormClosing;

            Controls.Add(BuildLayout());
            RefreshActionState();
        }

        internal ChatSession Session
        {
            get { return _context.Session; }
        }

        internal WorkspaceInfo Workspace
        {
            get { return _context.Workspace; }
        }

        internal bool IsBusy
        {
            get { return _isSending; }
        }

        internal ChatHistoryItem[] GetManualBrowseHistoryItems()
        {
            return IsDisposed || Disposing ? new ChatHistoryItem[0] : _chatWorkbench.GetHistoryItems();
        }

        internal void EnsureManualBrowseHistoryItemVisible(int index)
        {
            if (!IsDisposed && !Disposing)
            {
                _chatWorkbench.EnsureHistoryItemVisible(index);
            }
        }

        internal string[] GetManualBrowseActivityItems()
        {
            return IsDisposed || Disposing ? new string[0] : _activityBrowseControl.GetReadableItems();
        }

        internal void SetManualBrowseStatus(string text)
        {
            if (!IsDisposed && !Disposing)
            {
                SetStatus(text);
            }
        }

        internal void OpenManualBrowseMessageDetail(ChatHistoryItem item)
        {
            if (item != null && !IsDisposed && !Disposing)
            {
                _operations.WorkbenchCallbacks?.OpenMessageDetail?.Invoke(item);
            }
        }

        internal void SaveDraftForRuntimeTransition()
        {
            if (!_isClosingForSessionRemoval)
            {
                _operations.SaveDraft?.Invoke(_chatWorkbench.CaptureDraftState());
            }
        }

        internal bool HasPendingTargetOperation => _pendingSends > 0;

        internal void SaveStateForTargetExit()
        {
            _operations.SaveMessageSelection?.Invoke(_chatWorkbench.CaptureHistorySelection());
            SaveDraftForRuntimeTransition();
        }

        internal void CommitTargetExit()
        {
            _isClosingForTargetExit = true;
        }

        internal void RefreshSession(ChatSession session)
        {
            if (session == null || !string.Equals(session.Id, Session.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _context.RefreshSession(session);
            RefreshActionState();
        }

        private void RefreshWindowTitle()
        {
            var title = SessionWorkbenchTitleComposer.Build(Workspace, Session, IsBusy);
            if (!string.Equals(Text, title, StringComparison.Ordinal))
            {
                Text = title;
            }
        }

        internal void HandleAppServerEvent(AppServerEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(e.ThreadId) &&
                !string.Equals(Session.ThreadId, e.ThreadId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(e.ThreadId))
            {
                return;
            }

            if (SessionHistoryEventPolicy.ShouldInvalidateForEvent(e))
            {
                _operations.HistoryLoadWorkflow?.Invalidate(e.ThreadId);
            }

            ApplyActivityEvent(e);
            var route = AppServerEventRouting.Resolve(e);
            if (route == AppServerEventRoute.TurnStarted)
            {
                _isSending = true;
                SetStatus(e.Content);
                RefreshActionState();
                return;
            }

            if (AppServerEventRouting.IsTurnTerminal(route))
            {
                _isSending = false;
                SetStatus(e.Content);
                _ = LoadHistoryAsync(false);
                RefreshActionState();
                if (!AppServerEventRouting.IsErrorDisplayEvent(route))
                {
                    return;
                }
            }

            if (route == AppServerEventRoute.Assistant && AssistantMessageUtility.HasDisplayableContent(e.Content))
            {
                _chatWorkbench.AddHistoryItem(CreateHistoryItem("codex", e.Content, ChatHistoryCategory.Conversation));
                SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Status.ReplyReceived", "Reply received"));
                return;
            }

            if (AppServerEventRouting.IsErrorDisplayEvent(route))
            {
                _chatWorkbench.AddHistoryItem(CreateHistoryItem(
                    UiLocalization.Text("MainForm.Chat.Sender.Error", "Error"),
                    e.Content,
                    ChatHistoryCategory.Error));
                SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Status.ErrorOccurred", "An error occurred"));
            }
        }

        internal void CloseForSessionRemoval()
        {
            _isClosingForSessionRemoval = true;
            _operations.DiscardDraft?.Invoke();
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_operations.ManualBrowseContext != null &&
                _operations.ManualBrowseContext.HandleHotkey(keyData))
            {
                return true;
            }

            if (_operations.ManualBrowseContext != null && keyData == (Keys.Alt | Keys.Enter))
            {
                return true;
            }

            if (keyData == (Keys.Control | Keys.W))
            {
                Close();
                return true;
            }

            if (keyData == (Keys.Alt | Keys.M))
            {
                return _chatWorkbench.FocusMessages() || base.ProcessCmdKey(ref msg, keyData);
            }

            if (keyData == (Keys.Alt | Keys.I))
            {
                return _chatWorkbench.FocusInput() || base.ProcessCmdKey(ref msg, keyData);
            }

            if (keyData == (Keys.Alt | Keys.L))
            {
                return FocusActivity() || base.ProcessCmdKey(ref msg, keyData);
            }

            if (keyData == Keys.Tab && IsLastEnabledToolButtonFocused())
            {
                return _chatWorkbench.FocusMessages() || base.ProcessCmdKey(ref msg, keyData);
            }

            if (keyData == (Keys.Shift | Keys.Tab))
            {
                if (_activityBrowseControl.ContainsFocus)
                {
                    return _chatWorkbench.FocusLastInteractiveControl() || base.ProcessCmdKey(ref msg, keyData);
                }

                if (IsFirstEnabledToolButtonFocused())
                {
                    return FocusActivity() || base.ProcessCmdKey(ref msg, keyData);
                }
            }

            if (keyData == Keys.Escape)
            {
                if (_operations.CanCloseWithEscape == null || _operations.CanCloseWithEscape())
                {
                    Close();
                }

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool IsFirstEnabledToolButtonFocused()
        {
            if (_fileBrowserButton.Enabled)
            {
                return _fileBrowserButton.ContainsFocus;
            }

            if (_gitWorkbenchButton.Enabled)
            {
                return _gitWorkbenchButton.ContainsFocus;
            }

            if (_terminalButton.Enabled)
            {
                return _terminalButton.ContainsFocus;
            }

            return _subAgentsButton.Enabled && _subAgentsButton.ContainsFocus;
        }

        private bool IsLastEnabledToolButtonFocused()
        {
            if (_subAgentsButton.Enabled)
            {
                return _subAgentsButton.ContainsFocus;
            }

            if (_terminalButton.Enabled)
            {
                return _terminalButton.ContainsFocus;
            }

            if (_gitWorkbenchButton.Enabled)
            {
                return _gitWorkbenchButton.ContainsFocus;
            }

            return _fileBrowserButton.Enabled && _fileBrowserButton.ContainsFocus;
        }

        private bool FocusActivity()
        {
            return !_activityBrowseControl.IsDisposed &&
                   _activityBrowseControl.CanFocus &&
                   _activityBrowseControl.Focus();
        }

        private async Task InitializeAsync()
        {
            var draft = _operations.LoadDraft != null ? _operations.LoadDraft() : null;
            _chatWorkbench.RestoreDraftState(draft);
            _pendingMessageSelectionRestore = _operations.LoadMessageSelection != null
                ? _operations.LoadMessageSelection()
                : null;
            _chatWorkbench.FocusMessages();
            await LoadHistoryAsync(true);
        }

        private ChatWorkbenchHostCallbacks CreateWorkbenchCallbacks()
        {
            var callbacks = _operations.WorkbenchCallbacks ?? new ChatWorkbenchHostCallbacks();
            return new ChatWorkbenchHostCallbacks
            {
                SelectAttachmentsAsync = callbacks.SelectAttachmentsAsync,
                SelectImageAttachmentsAsync = callbacks.SelectImageAttachmentsAsync,
                SelectFileAttachmentsAsync = callbacks.SelectFileAttachmentsAsync,
                SelectFolderAttachmentsAsync = callbacks.SelectFolderAttachmentsAsync,
                SelectSkillAttachmentsAsync = callbacks.SelectSkillAttachmentsAsync,
                SelectPluginAttachmentsAsync = callbacks.SelectPluginAttachmentsAsync,
                CanPasteAttachments = callbacks.CanPasteAttachments,
                PasteAttachmentsAsync = callbacks.PasteAttachmentsAsync,
                ValidateAttachmentsAsync = callbacks.ValidateAttachmentsAsync,
                ReadImageBytes = callbacks.ReadImageBytes,
                OpenMessageDetail = callbacks.OpenMessageDetail,
                DeleteTemporaryAttachment = callbacks.DeleteTemporaryAttachment,
                GetSendMessageShortcutMode = callbacks.GetSendMessageShortcutMode,
                SendAsync = SendCurrentInputAsync,
                StopAsync = StopAsync,
                CanSend = () => !_isLoadingHistory,
                CanStop = () => _isSending
            };
        }

        private async Task LoadHistoryAsync(bool includeSystemHeader)
        {
            if (_operations.HistoryLoadWorkflow == null || IsDisposed || Disposing)
            {
                return;
            }

            _isLoadingHistory = true;
            RefreshActionState();
            try
            {
                var request = _operations.HistoryLoadWorkflow.BuildRequest(Session, includeSystemHeader);
                ApplyHistoryLoadResult(request.ImmediateResult);
                if (!request.RequiresRemoteRead)
                {
                    return;
                }

                var result = await _operations.HistoryLoadWorkflow.CompleteRemoteLoadAsync(
                    request,
                    IsCurrentHistoryTarget,
                    _ => false);
                ApplyHistoryLoadResult(result);
            }
            finally
            {
                _isLoadingHistory = false;
                RefreshActionState();
            }
        }

        private bool IsCurrentHistoryTarget(ChatSession session, string threadId)
        {
            return !IsDisposed &&
                   session != null &&
                   string.Equals(session.Id, Session.Id, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(threadId, Session.ThreadId, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyHistoryLoadResult(SessionHistoryLoadResult result)
        {
            if (result == null || result.Kind == SessionHistoryLoadResultKind.Stale || IsDisposed)
            {
                return;
            }

            if (result.ShouldReplaceHistory &&
                !(result.Kind == SessionHistoryLoadResultKind.LoadingRemoteHistory &&
                  _chatWorkbench.GetHistoryItems().Length > 0))
            {
                var historyItems = result.Kind == SessionHistoryLoadResultKind.LoadedRemoteHistory
                    ? SessionHistoryOptimisticMerge.PreserveLocalConversationItems(
                        _chatWorkbench.GetHistoryItems(),
                        result.HistoryItems)
                    : result.HistoryItems;
                _chatWorkbench.ReplaceHistoryItems(historyItems);
                RestorePendingMessageSelection();
            }

            if (result.HasStatus)
            {
                SetStatus(result.StatusText);
            }
        }

        private async Task<ChatWorkbenchSendResult> SendCurrentInputAsync(ChatWorkbenchSendRequest request)
        {
            if (_operations.SendAsync == null)
            {
                return new ChatWorkbenchSendResult(false, false, string.Empty);
            }

            _pendingSends++;
            _isSending = true;
            ResetActivityBeforeSend();
            _chatWorkbench.AddHistoryItem(CreateHistoryItem(
                UiLocalization.Text("MainForm.Chat.Sender.User", "Me"),
                request != null ? request.PreviewText : string.Empty,
                ChatHistoryCategory.Conversation));
            RefreshActionState();
            try
            {
                var result = await _operations.SendAsync(Session, request);
                if (result == null || !result.KeepSending)
                {
                    _isSending = false;
                }

                if (result != null && result.IsSuccess)
                {
                    _operations.PlayMessageSentSound?.Invoke();
                }

                if (result != null && result.IsSuccess && result.ClearInput)
                {
                    _operations.ClearDraft?.Invoke();
                }

                _operations.PersistState?.Invoke();
                return result ?? new ChatWorkbenchSendResult(false, false, string.Empty);
            }
            catch (Exception ex)
            {
                _isSending = false;
                _chatWorkbench.AddHistoryItem(CreateHistoryItem(
                    UiLocalization.Text("MainForm.Chat.Sender.Error", "Error"),
                    ex.Message,
                    ChatHistoryCategory.Error));
                return new ChatWorkbenchSendResult(
                    false,
                    false,
                    UiLocalization.Text("SubAgentWorkbenchForm.Status.SendFailed", "Send failed"));
            }
            finally
            {
                _pendingSends--;
                RefreshActionState();
            }
        }

        private async Task StopAsync()
        {
            if (_operations.StopAsync == null)
            {
                return;
            }

            try
            {
                SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Status.RequestingStop", "Requesting stop..."));
                await _operations.StopAsync(Session);
                SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Status.StopRequested", "Stop requested"));
            }
            catch (Exception ex)
            {
                _chatWorkbench.AddHistoryItem(CreateHistoryItem(
                    UiLocalization.Text("MainForm.Chat.Sender.Error", "Error"),
                    ex.Message,
                    ChatHistoryCategory.Error));
                SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Status.StopFailed", "Stop failed"));
            }
        }

        private void ApplyActivityEvent(AppServerEventArgs e)
        {
            var summary = EnsureTurnSummaryState(e.ThreadId);
            var summaryChanged = TurnProgressSummaryReducer.ApplyEvent(summary, e);
            var timelineChanged = EnsureActivityTimelineState(e.ThreadId).ApplyEvent(e);
            if (summaryChanged || timelineChanged)
            {
                RefreshActivityDisplay();
            }
        }

        private void ResetActivityBeforeSend()
        {
            if (string.IsNullOrWhiteSpace(Session.ThreadId))
            {
                return;
            }

            TurnProgressSummaryReducer.ResetForPreparing(EnsureTurnSummaryState(Session.ThreadId));
            EnsureActivityTimelineState(Session.ThreadId).Clear();
            RefreshActivityDisplay();
        }

        private TurnProgressSummaryState EnsureTurnSummaryState(string threadId)
        {
            TurnProgressSummaryState state;
            if (!_turnSummaryStates.TryGetValue(threadId, out state))
            {
                state = new TurnProgressSummaryState();
                _turnSummaryStates[threadId] = state;
            }

            return state;
        }

        private ActivityTimelineState EnsureActivityTimelineState(string threadId)
        {
            ActivityTimelineState state;
            if (!_activityTimelineStates.TryGetValue(threadId, out state))
            {
                state = new ActivityTimelineState();
                _activityTimelineStates[threadId] = state;
            }

            return state;
        }

        private void RefreshActivityDisplay()
        {
            if (string.IsNullOrWhiteSpace(Session.ThreadId))
            {
                _activityCurrentStatusLabel.Text = UiLocalization.Text("MainForm.Activity.Current.Empty", "No active activity currently.");
                _activityBrowseControl.SetItems(new[] { UiLocalization.Text("MainForm.Activity.List.Empty", "No activity records currently.") });
                return;
            }

            TurnProgressSummaryState summary;
            _turnSummaryStates.TryGetValue(Session.ThreadId, out summary);
            ActivityTimelineState timeline;
            _activityTimelineStates.TryGetValue(Session.ThreadId, out timeline);
            var summaryText = TurnProgressSummaryReducer.BuildSummaryText(summary);
            _activityCurrentStatusLabel.Text = ActivityDisplayState.BuildCurrentStatus(
                summaryText,
                UiLocalization.Text("MainForm.Activity.Current.Empty", "No active activity currently."));
            _activityBrowseControl.SetItems(timeline == null
                ? ActivityDisplayState.BuildStableItems(summaryText, UiLocalization.Text("MainForm.Activity.List.Empty", "No activity records currently."))
                : timeline.BuildDisplayItems(UiLocalization.Text("MainForm.Activity.List.Empty", "No activity records currently.")));
        }

        private void RefreshActionState()
        {
            RefreshWindowTitle();
            _chatWorkbench.SetSendingState(_isSending);
            _chatWorkbench.RefreshActionState();
            _fileBrowserButton.Enabled = _operations.OpenFileBrowser != null;
            _gitWorkbenchButton.Enabled = _operations.OpenGitWorkbench != null;
            _terminalButton.Enabled = _operations.OpenTerminal != null;
            _subAgentsButton.Enabled = _operations.OpenSubAgents != null &&
                (_operations.CanOpenSubAgents == null || _operations.CanOpenSubAgents());
        }

        private void SessionWorkbenchForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!e.Cancel && !_isClosingForSessionRemoval && !_isClosingForTargetExit)
            {
                _operations.SaveMessageSelection?.Invoke(_chatWorkbench.CaptureHistorySelection());
                SaveDraftForRuntimeTransition();
            }
        }

        private void RestorePendingMessageSelection()
        {
            if (_pendingMessageSelectionRestore == null ||
                !_chatWorkbench.RestoreHistorySelection(_pendingMessageSelectionRestore))
            {
                return;
            }

            _pendingMessageSelectionRestore = null;
            _chatWorkbench.FocusMessages();
        }

        private void SetStatus(string text)
        {
            _chatWorkbench.SetStatus(text);
        }

        private Control BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(BuildToolPanel(), 0, 0);
            root.Controls.Add(BuildWorkbenchPanel(), 0, 1);
            return root;
        }

        private Control BuildToolPanel()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = UiTheme.WindowBackColor,
                Margin = new Padding(0, 0, 0, UiTheme.ControlSpacing),
                TabStop = false
            };
            panel.Controls.Add(_fileBrowserButton);
            panel.Controls.Add(_gitWorkbenchButton);
            panel.Controls.Add(_terminalButton);
            panel.Controls.Add(_subAgentsButton);
            return panel;
        }

        private Control BuildWorkbenchPanel()
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                FixedPanel = FixedPanel.Panel2,
                SplitterDistance = 430,
                TabStop = false
            };
            split.Panel1.TabStop = false;
            split.Panel2.TabStop = false;
            _chatWorkbench.Dock = DockStyle.Fill;
            split.Panel1.Controls.Add(_chatWorkbench);
            split.Panel2.Controls.Add(BuildActivityPanel());
            return split;
        }

        private Control BuildActivityPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.PanelBackColor,
                TabStop = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var title = new Label
            {
                Text = UiLocalization.Text("MainForm.Label.Activity", "Activity") + "(&L)",
                AccessibleName = UiLocalization.Text("MainForm.Accessible.Activity", "Activity"),
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            _activityBrowseControl.Dock = DockStyle.Fill;
            panel.Controls.Add(title, 0, 0);
            panel.Controls.Add(_activityCurrentStatusLabel, 0, 1);
            panel.Controls.Add(_activityBrowseControl, 0, 2);
            return panel;
        }

        private static Button CreateToolButton(string text, string accessibleName, int tabIndex, UiGlyph glyph)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 118,
                Height = 32,
                Margin = new Padding(0, 0, UiTheme.ControlSpacing, 0),
                TabIndex = tabIndex,
                UseMnemonic = true
            };
            UiTheme.ApplyButton(button, glyph);
            return button;
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
            return new ChatHistoryItem
            {
                Sender = sender,
                Category = category,
                DisplayText = string.Format("{0} {1}", prefix, displayMessage).Trim(),
                FullText = string.Format("{0} {1}", prefix, normalized).Trim(),
                AccessibleText = string.Format("{0} {1}", prefix, accessibleMessage).Trim(),
                MarkdownText = normalized
            };
        }
    }
}
