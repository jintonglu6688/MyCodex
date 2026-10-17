using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitConflictDetailsForm : Form, IRuntimeTransitionWindow
    {
        bool IRuntimeTransitionWindow.IsBusyForRuntimeTransition => _isLoading;

        private readonly string _workspacePath;
        private readonly IGitDiffReader _gitService;
        private readonly GitChangedFileEntry _entry;
        private readonly Label _statusLabel;
        private readonly TextBox _detailsTextBox;
        private readonly Button _refreshButton;
        private readonly Button _copyButton;
        private readonly Button _closeButton;

        private bool _isLoading;

        public GitConflictDetailsForm(string workspacePath, IGitDiffReader gitService, GitChangedFileEntry entry)
        {
            _workspacePath = workspacePath;
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));

            Text = UiLocalization.Text("GitConflictDetailsForm.Title", "Conflict Details");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(860, 520);
            Size = new Size(980, 620);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, true);

            _detailsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitConflictDetailsForm.Accessible.Details", "Details"),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_detailsTextBox, true, true);

            _refreshButton = CreateButton(UiLocalization.Text("GitConflictDetailsForm.Button.Refresh", "Refresh(&R)"), UiLocalization.Text("GitConflictDetailsForm.Accessible.Refresh", "Refresh"), 1);
            _copyButton = CreateButton(UiLocalization.Text("GitConflictDetailsForm.Button.Copy", "Copy(&C)"), UiLocalization.Text("GitConflictDetailsForm.Accessible.Copy", "Copy"), 2);
            _closeButton = CreateButton(UiLocalization.Text("GitConflictDetailsForm.Button.Close", "Close(&E)"), UiLocalization.Text("GitConflictDetailsForm.Accessible.Close", "Close"), 3);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_copyButton, UiGlyph.CommandOutput);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _refreshButton.Click += async (sender, e) => await LoadDetailsAsync();
            _copyButton.Click += (sender, e) => CopyDetails();
            _closeButton.Click += (sender, e) => Close();
            KeyDown += GitConflictDetailsForm_KeyDown;
            Shown += async (sender, e) =>
            {
                Activate();
                await LoadDetailsAsync();
            };

            BuildLayout();
            CancelButton = _closeButton;
            SetStatus(string.Format(
                UiLocalization.Text("GitConflictDetailsForm.Status.Preparing.Template", "Preparing conflict details for {0}..."),
                _entry.FilePath));
        }

        private async void GitConflictDetailsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                e.SuppressKeyPress = true;
                await LoadDetailsAsync();
            }
        }

        private async Task LoadDetailsAsync()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            try
            {
                RefreshButtons();
                SetStatus(string.Format(
                    UiLocalization.Text("GitConflictDetailsForm.Status.Loading.Template", "Loading conflict details for {0}..."),
                    _entry.FilePath));
                var result = await _gitService.ReadConflictDetailsAsync(_workspacePath, _entry).ConfigureAwait(true);
                if (result == null || !result.Success)
                {
                    var message = result == null ? string.Empty : result.DisplayMessage;
                    TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, message);
                    SetStatus(message);
                    MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, result.StandardOutput ?? string.Empty);
                SetStatus(result.DisplayMessage);
            }
            catch (Exception ex)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, ex.Message);
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
                RestoreFocus();
            }
        }

        private void CopyDetails()
        {
            var text = _detailsTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Clipboard.SetText(text);
            SetStatus(UiLocalization.Text("GitConflictDetailsForm.Status.Copied", "Copied conflict details."));
            RestoreFocus();
        }

        private void RefreshButtons()
        {
            _refreshButton.Enabled = !_isLoading;
            _copyButton.Enabled = !_isLoading && !string.IsNullOrWhiteSpace(_detailsTextBox.Text);
        }

        private void RestoreFocus()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                Activate();
                if (_detailsTextBox.CanFocus)
                {
                    _detailsTextBox.Focus();
                    return;
                }

                if (_refreshButton.Enabled && _refreshButton.CanFocus)
                {
                    _refreshButton.Focus();
                }
            });
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text ?? string.Empty;
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_refreshButton);
            footer.Controls.Add(_copyButton);

            root.Controls.Add(_statusLabel, 0, 0);
            root.Controls.Add(_detailsTextBox, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 118,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }
    }
}
