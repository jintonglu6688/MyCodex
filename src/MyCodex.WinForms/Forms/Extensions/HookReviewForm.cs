using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    /// <summary>
    /// Startup review that mirrors the official CLI: list every new or changed hook, then trust all,
    /// open management, or continue without trusting. Trust all only covers the hashes shown here.
    /// </summary>
    public sealed class HookReviewForm : Form
    {
        private readonly HookManagementService _service;
        private readonly string _workspacePath;
        private readonly string _threadId;
        private readonly Label _summaryLabel;
        private readonly ListView _hookListView;
        private readonly TextBox _detailsTextBox;
        private readonly Button _trustAllButton;
        private readonly Button _manageButton;
        private readonly Button _laterButton;
        private List<AppServerHookInfo> _pending;
        private bool _isBusy;

        public HookReviewForm(HookManagementService service, string workspacePath, string threadId, List<AppServerHookInfo> pending)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _workspacePath = workspacePath;
            _threadId = threadId;

            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 480);
            Size = new Size(940, 600);
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);

            _summaryLabel = UiControlFactory.CreateFillAutoSizeLabel(string.Empty);
            _hookListView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details,
                AccessibleName = UiLocalization.Text("HookReviewForm.Accessible.Hooks", "Hooks needing review"),
                TabIndex = 0
            };
            UiTheme.ApplyListView(_hookListView);
            HooksManagementForm.AddColumns(_hookListView);

            _detailsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = UiLocalization.Text("HooksManagementForm.Accessible.Details", "Details"),
                TabIndex = 1
            };
            UiTheme.ApplyTextBox(_detailsTextBox, true, true);

            _trustAllButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("HookReviewForm.Button.TrustAll", "Trust All(&A)..."),
                UiLocalization.Text("HookReviewForm.Accessible.TrustAll", "Trust all"),
                2,
                UiGlyph.Ok);
            _manageButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("HookReviewForm.Button.Manage", "Manage(&M)..."),
                UiLocalization.Text("HookReviewForm.Accessible.Manage", "Manage hooks"),
                3,
                UiGlyph.Approval);
            _laterButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("HookReviewForm.Button.Later", "Later(&L)"),
                UiLocalization.Text("HookReviewForm.Accessible.Later", "Continue without trusting"),
                4,
                UiGlyph.Cancel,
                DialogResult.Cancel);

            _hookListView.SelectedIndexChanged += (sender, e) =>
                _detailsTextBox.Text = HooksManagementForm.BuildDetails(SelectedHook);
            _trustAllButton.Click += async (sender, e) => await TrustAllAsync();
            _manageButton.Click += (sender, e) =>
            {
                OpenManagementRequested = true;
                DialogResult = DialogResult.OK;
            };
            Shown += (sender, e) => _hookListView.Focus();

            BuildLayout();
            CancelButton = _laterButton;
            ShowPending(pending);
        }

        /// <summary>The user chose to review hooks one by one in the management window.</summary>
        public bool OpenManagementRequested { get; private set; }

        private AppServerHookInfo SelectedHook
        {
            get { return _hookListView.SelectedItems.Count == 0 ? null : _hookListView.SelectedItems[0].Tag as AppServerHookInfo; }
        }

        private void BuildLayout()
        {
            var buttons = UiControlFactory.CreateDialogButtonBar();
            buttons.Controls.AddRange(new Control[] { _laterButton, _manageButton, _trustAllButton });

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_summaryLabel, 0, 0);
            root.Controls.Add(_hookListView, 0, 1);
            root.Controls.Add(_detailsTextBox, 0, 2);
            root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);
        }

        private void ShowPending(List<AppServerHookInfo> pending)
        {
            _pending = pending ?? new List<AppServerHookInfo>();
            Text = string.Format(
                CultureInfo.CurrentCulture,
                UiLocalization.Text("HookReviewForm.Title", "{0} hooks need review"),
                _pending.Count);
            _summaryLabel.Text = UiLocalization.Text(
                "HookReviewForm.Summary",
                "These hooks are new or changed and will not run until trusted. Hooks can run outside the sandbox after you trust them.");

            _hookListView.BeginUpdate();
            _hookListView.Items.Clear();
            foreach (var hook in _pending)
            {
                _hookListView.Items.Add(HooksManagementForm.CreateItem(hook));
            }

            if (_hookListView.Items.Count > 0)
            {
                _hookListView.Items[0].Selected = true;
                _hookListView.Items[0].Focused = true;
            }

            _hookListView.EndUpdate();
            _detailsTextBox.Text = HooksManagementForm.BuildDetails(SelectedHook);
            _trustAllButton.Enabled = !_isBusy && _pending.Count > 0;
        }

        private async Task TrustAllAsync()
        {
            if (_isBusy || _pending.Count == 0)
            {
                return;
            }

            var confirm = string.Format(
                CultureInfo.CurrentCulture,
                UiLocalization.Text("HookReviewForm.Confirm.TrustAll", "Trust all {0} hooks listed here? They will run until their definitions change."),
                _pending.Count);
            if (MessageBox.Show(this, confirm, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            _isBusy = true;
            _trustAllButton.Enabled = false;
            try
            {
                var result = await _service.TrustAsync(_workspacePath, _threadId, _pending);
                switch (result.Status)
                {
                    case HookStateChangeStatus.Saved:
                        DialogResult = DialogResult.OK;
                        return;
                    case HookStateChangeStatus.Changed:
                        // Never approve a definition the user has not seen: show the new set and ask again.
                        _isBusy = false;
                        ShowPending(HookReviewPromptState.GetPending(result.List));
                        ShowMessage(UiLocalization.Text("HookReviewForm.Message.Changed", "Some hooks changed while this list was open. Review the updated list before trusting."));
                        return;
                    default:
                        ShowMessage(UiLocalization.Text("HooksManagementForm.Status.VersionUnavailable", "The user configuration version could not be confirmed. Nothing was saved."));
                        return;
                }
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message);
            }
            finally
            {
                _isBusy = false;
                _trustAllButton.Enabled = _pending.Count > 0;
            }
        }

        private void ShowMessage(string message)
        {
            MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _hookListView.Focus();
        }
    }
}
