using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class AutomationRunDetailsForm : Form
    {
        private readonly AutomationRunSnapshot _run;
        private readonly AutomationRunActionCallbacks _callbacks;
        private readonly AutomationRunActivityCallbacks _activityCallbacks;
        private readonly ChatHistoryListBox _conversationListBox;
        private readonly ActivityVirtualBrowseControl _activityBrowseControl;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly Button _openThreadButton;
        private readonly Button _openWorkspaceButton;
        private readonly Button _closeButton;
        private IDisposable _activitySubscription;
        private bool _conversationLoadStarted;
        private bool _conversationLoadInProgress;
        private bool _conversationReloadPending;

        public AutomationRunDetailsForm(
            AutomationRunSnapshot run,
            string automationName,
            AutomationRunActionCallbacks callbacks)
            : this(run, automationName, callbacks, null)
        {
        }

        public AutomationRunDetailsForm(
            AutomationRunSnapshot run,
            string automationName,
            AutomationRunActionCallbacks callbacks,
            AutomationRunActivityCallbacks activityCallbacks)
        {
            _run = run ?? new AutomationRunSnapshot();
            _callbacks = callbacks;
            _activityCallbacks = activityCallbacks;

            Text = UiLocalization.Text("AutomationRunDetailsForm.Title", "Automation run details");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(780, 620);
            Size = new Size(980, 740);
            UiTheme.ApplyForm(this);

            _conversationListBox = CreateConversationListBox();
            ReplaceConversationItems(new[]
            {
                CreateConversationItem(
                    ChatHistoryItemFactory.GetSystemSenderText(),
                    UiLocalization.Text("AutomationRunDetailsForm.Conversation.NotLoaded", "Messages have not been loaded yet."),
                    ChatHistoryCategory.System)
            }, true);

            _activityBrowseControl = CreateActivityBrowseControl();
            SetActivityItems(GetInitialActivityItems());

            _statusLabel = new Label
            {
                AccessibleName = UiLocalization.Text("AutomationRunDetailsForm.Accessible.Status", "Status"),
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TabStop = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = UiLocalization.Text("AutomationRunDetailsForm.Status.Ready", "Ready.")
            };
            UiTheme.ApplyLabel(_statusLabel, false);

            _refreshButton = CreateButton(UiLocalization.Text("AutomationRunDetailsForm.Button.Refresh", "Refresh(&R)"), 2, UiGlyph.Refresh);
            _openThreadButton = CreateButton(UiLocalization.Text("AutomationRunDetailsForm.Button.OpenThread", "Open in main window(&T)"), 3, UiGlyph.Info);
            _openWorkspaceButton = CreateButton(UiLocalization.Text("AutomationRunDetailsForm.Button.OpenWorkspace", "Open workspace(&W)"), 4, UiGlyph.Workspace);
            _closeButton = CreateButton(UiLocalization.Text("AutomationRunDetailsForm.Button.Close", "Close(&C)"), 5, UiGlyph.Ok);
            _closeButton.DialogResult = DialogResult.OK;

            _refreshButton.Click += async (sender, e) => await LoadConversationAsync(true).ConfigureAwait(true);
            _openThreadButton.Click += async (sender, e) => await OpenThreadAsync();
            _openWorkspaceButton.Click += async (sender, e) => await OpenWorkspaceAsync();
            _closeButton.Click += (sender, e) => Close();
            _conversationListBox.SelectionSummaryRequested += ConversationListBox_SelectionSummaryRequested;

            BuildLayout(automationName);
            SubscribeActivityUpdates();
            UpdateButtonState();
        }

        private void BuildLayout(string automationName)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 62F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(CreateInfoPanel(automationName), 0, 0);
            root.Controls.Add(CreateLabeledPanel(UiLocalization.Text("AutomationRunDetailsForm.Group.Conversation", "Messages"), _conversationListBox), 0, 1);
            root.Controls.Add(CreateActivityPanel(), 0, 2);
            root.Controls.Add(_statusLabel, 0, 3);
            root.Controls.Add(CreateButtonPanel(), 0, 4);

            Controls.Add(root);
        }

        private Control CreateInfoPanel(string automationName)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 6,
                TabStop = false
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            AddInfoRow(grid, 0, UiLocalization.Text("AutomationRunDetailsForm.Label.Name", "Name"), automationName);
            AddInfoRow(grid, 1, UiLocalization.Text("AutomationRunDetailsForm.Label.Status", "Status"), AutomationDisplayText.RunStatus(_run.Status));
            AddInfoRow(grid, 2, UiLocalization.Text("AutomationRunDetailsForm.Label.StartedAt", "Started"), FormatDate(_run.StartedAt));
            AddInfoRow(grid, 3, UiLocalization.Text("AutomationRunDetailsForm.Label.CompletedAt", "Completed"), FormatDate(_run.CompletedAt));
            AddInfoRow(grid, 4, UiLocalization.Text("AutomationRunDetailsForm.Label.ActualModel", "Model"), FormatModel(_run.ActualModelId ?? _run.ConfiguredModelId, _run.ActualReasoningEffort ?? _run.ConfiguredReasoningEffort));
            AddInfoRow(grid, 5, UiLocalization.Text("AutomationRunDetailsForm.Label.WorkspacePath", "Workspace path"), _run.WorkspacePath);
            return grid;
        }

        private static void AddInfoRow(TableLayoutPanel grid, int row, string labelText, string valueText)
        {
            var label = new Label
            {
                Text = labelText,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TabStop = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            UiTheme.ApplyLabel(label, true);

            var value = new Label
            {
                Text = valueText ?? string.Empty,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                TabStop = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            UiTheme.ApplyLabel(value, false);

            grid.Controls.Add(label, 0, row);
            grid.Controls.Add(value, 1, row);
        }

        private Control CreateActivityPanel()
        {
            return CreateLabeledPanel(
                UiLocalization.Text("AutomationRunDetailsForm.Group.Activity", "Activity"),
                _activityBrowseControl);
        }

        private Control CreateButtonPanel()
        {
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                TabStop = false
            };
            buttons.Controls.Add(_closeButton);
            buttons.Controls.Add(_openWorkspaceButton);
            buttons.Controls.Add(_openThreadButton);
            buttons.Controls.Add(_refreshButton);
            return buttons;
        }

        private static Control CreateLabeledPanel(string labelText, Control control)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                TabStop = false
            };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var label = new Label
            {
                Text = labelText,
                AutoSize = true,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);

            panel.Controls.Add(label, 0, 0);
            panel.Controls.Add(control, 0, 1);
            return panel;
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SubscribeActivityUpdates();
            if (_conversationLoadStarted)
            {
                return;
            }

            _conversationLoadStarted = true;
            await LoadConversationAsync(false).ConfigureAwait(true);
        }

        protected override void OnClosed(EventArgs e)
        {
            DisposeActivitySubscription();
            base.OnClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeActivitySubscription();
            }

            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static ChatHistoryListBox CreateConversationListBox()
        {
            var listBox = new ChatHistoryListBox
            {
                AccessibleName = UiLocalization.Text("AutomationRunDetailsForm.Accessible.Conversation", "Messages"),
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                IntegralHeight = false,
                TabIndex = 0
            };
            UiTheme.ApplyListBox(listBox);
            return listBox;
        }

        private static ActivityVirtualBrowseControl CreateActivityBrowseControl()
        {
            return new ActivityVirtualBrowseControl
            {
                AccessibleName = UiLocalization.Text("AutomationRunDetailsForm.Accessible.Activity", "Activity"),
                Dock = DockStyle.Fill,
                TabIndex = 1
            };
        }

        private static Button CreateButton(string text, int tabIndex, UiGlyph glyph)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = StripMnemonic(text),
                AutoSize = true,
                TabIndex = tabIndex,
                UseMnemonic = true
            };
            UiTheme.ApplyDialogButton(button, glyph);
            return button;
        }

        private IReadOnlyList<string> GetInitialActivityItems()
        {
            if (_activityCallbacks == null ||
                _activityCallbacks.GetActivityItems == null ||
                string.IsNullOrWhiteSpace(_run.ThreadId))
            {
                return new[] { UiLocalization.Text("AutomationRunDetailsForm.Activity.Empty", "No activity records currently.") };
            }

            var items = _activityCallbacks.GetActivityItems(_run.ThreadId);
            return items == null || items.Count == 0
                ? new[] { UiLocalization.Text("AutomationRunDetailsForm.Activity.Empty", "No activity records currently.") }
                : items;
        }

        private void SetActivityItems(IReadOnlyList<string> items)
        {
            _activityBrowseControl.SetItems(items == null || items.Count == 0
                ? new[] { UiLocalization.Text("AutomationRunDetailsForm.Activity.Empty", "No activity records currently.") }
                : items);
        }

        private void SubscribeActivityUpdates()
        {
            if (_activitySubscription != null ||
                _activityCallbacks == null ||
                _activityCallbacks.SubscribeActivityUpdates == null ||
                string.IsNullOrWhiteSpace(_run.ThreadId))
            {
                return;
            }

            _activitySubscription = _activityCallbacks.SubscribeActivityUpdates(_run.ThreadId, OnActivityItemsUpdated);
        }

        private void DisposeActivitySubscription()
        {
            var subscription = _activitySubscription;
            if (subscription == null)
            {
                return;
            }

            _activitySubscription = null;
            subscription.Dispose();
        }

        private void OnActivityItemsUpdated(IReadOnlyList<string> items)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<IReadOnlyList<string>>(OnActivityItemsUpdated), items);
                return;
            }

            SetActivityItems(items);
            _statusLabel.Text = UiLocalization.Text("AutomationRunDetailsForm.Status.ActivityUpdated", "Activity updated.");
            _ = LoadConversationAsync(false);
        }

        private void ConversationListBox_SelectionSummaryRequested(object sender, ChatHistorySelectionSummaryEventArgs e)
        {
            if (e != null && !string.IsNullOrWhiteSpace(e.SummaryText))
            {
                _statusLabel.Text = e.SummaryText;
            }
        }

        private async Task LoadConversationAsync(bool manualRefresh)
        {
            if (_conversationLoadInProgress)
            {
                _conversationReloadPending = true;
                return;
            }

            _conversationLoadInProgress = true;
            _refreshButton.Enabled = false;
            try
            {
                if (manualRefresh)
                {
                    ReplaceConversationItems(new[]
                    {
                        CreateConversationItem(
                            ChatHistoryItemFactory.GetSystemSenderText(),
                            UiLocalization.Text("AutomationRunDetailsForm.Conversation.Loading", "Loading messages..."),
                            ChatHistoryCategory.System)
                    }, true);
                }

                if (_callbacks == null || _callbacks.LoadConversationAsync == null)
                {
                    ReplaceConversationItems(new[]
                    {
                        CreateConversationItem(
                            ChatHistoryItemFactory.GetSystemSenderText(),
                            UiLocalization.Text("AutomationRunDetailsForm.Conversation.Unavailable", "Messages are not available."),
                            ChatHistoryCategory.System)
                    }, true);
                    _statusLabel.Text = UiLocalization.Text("AutomationRunDetailsForm.Status.ConversationUnavailable", "Messages are unavailable.");
                    return;
                }

                if (!manualRefresh)
                {
                    _statusLabel.Text = UiLocalization.Text("AutomationRunDetailsForm.Status.LoadingConversation", "Loading messages...");
                }

                var items = await _callbacks.LoadConversationAsync(_run).ConfigureAwait(true);
                var safeItems = (items ?? new ChatHistoryItem[0]).Where(item => item != null).ToArray();
                if (safeItems.Length == 0)
                {
                    safeItems = new[]
                    {
                        CreateConversationItem(
                            ChatHistoryItemFactory.GetSystemSenderText(),
                            UiLocalization.Text("MainForm.Message.NoHistoryMessages", "No history messages."),
                            ChatHistoryCategory.System)
                    };
                }

                ReplaceConversationItems(safeItems, false);
                _statusLabel.Text = manualRefresh
                    ? UiLocalization.Text("AutomationRunDetailsForm.Status.Refreshed", "Messages refreshed.")
                    : UiLocalization.Text("AutomationRunDetailsForm.Status.Loaded", "Messages loaded.");
            }
            catch (Exception ex)
            {
                ReplaceConversationItems(new[]
                {
                    CreateConversationItem(
                        ChatHistoryItemFactory.GetErrorSenderText(),
                        string.Format(
                            UiLocalization.Text("AutomationRunDetailsForm.Conversation.LoadFailed.Template", "Failed to load messages: {0}"),
                            ex.Message),
                        ChatHistoryCategory.Error)
                }, true);
                _statusLabel.Text = string.Format(
                    UiLocalization.Text("AutomationRunDetailsForm.Status.LoadFailed.Template", "Failed to load messages: {0}"),
                    ex.Message);
            }
            finally
            {
                _conversationLoadInProgress = false;
                _refreshButton.Enabled = true;
                UpdateButtonState();
            }

            if (_conversationReloadPending)
            {
                _conversationReloadPending = false;
                await LoadConversationAsync(false).ConfigureAwait(true);
            }
        }

        private void ReplaceConversationItems(IEnumerable<ChatHistoryItem> items, bool forceBottom)
        {
            var wasAtBottom = forceBottom ||
                              _conversationListBox.Items.Count == 0 ||
                              _conversationListBox.TopIndex >= Math.Max(0, _conversationListBox.Items.Count - 2);
            var previousSelectedIndex = _conversationListBox.SelectedIndex;
            var previousTopIndex = _conversationListBox.TopIndex;

            _conversationListBox.BeginUpdate();
            try
            {
                _conversationListBox.Items.Clear();
                foreach (var item in items ?? new ChatHistoryItem[0])
                {
                    if (item != null)
                    {
                        _conversationListBox.Items.Add(item);
                    }
                }

                if (_conversationListBox.Items.Count == 0)
                {
                    return;
                }

                if (wasAtBottom)
                {
                    _conversationListBox.TopIndex = _conversationListBox.Items.Count - 1;
                }
                else
                {
                    _conversationListBox.TopIndex = Math.Min(previousTopIndex, _conversationListBox.Items.Count - 1);
                    if (previousSelectedIndex >= 0)
                    {
                        _conversationListBox.SelectedIndex = Math.Min(previousSelectedIndex, _conversationListBox.Items.Count - 1);
                    }
                }
            }
            finally
            {
                _conversationListBox.EndUpdate();
            }
        }

        private static ChatHistoryItem CreateConversationItem(string sender, string message, ChatHistoryCategory category)
        {
            return ChatHistoryItemFactory.Create(sender, message, category);
        }

        private void UpdateButtonState()
        {
            _openThreadButton.Enabled = CanOpenThread();
            _openWorkspaceButton.Enabled = CanOpenPath(_run.WorkspacePath);
        }

        private bool CanOpenThread()
        {
            return _callbacks != null &&
                   _callbacks.OpenThreadAsync != null &&
                   _callbacks.CanOpenThread != null &&
                   _callbacks.CanOpenThread(_run);
        }

        private bool CanOpenPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   _callbacks != null &&
                   _callbacks.OpenPathAsync != null;
        }

        private async Task OpenThreadAsync()
        {
            if (!CanOpenThread())
            {
                return;
            }

            await _callbacks.OpenThreadAsync(_run).ConfigureAwait(true);
        }

        private async Task OpenWorkspaceAsync()
        {
            if (!CanOpenPath(_run.WorkspacePath))
            {
                return;
            }

            await _callbacks.OpenPathAsync(_run.WorkspacePath).ConfigureAwait(true);
        }

        private static string FormatDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty;
        }

        private static string FormatModel(string modelId, string reasoningEffort)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(reasoningEffort)
                ? modelId.Trim()
                : modelId.Trim() + " (" + reasoningEffort.Trim() + ")";
        }

        private static string StripMnemonic(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Replace("&", string.Empty).Replace("...", string.Empty).Trim();
        }
    }
}
