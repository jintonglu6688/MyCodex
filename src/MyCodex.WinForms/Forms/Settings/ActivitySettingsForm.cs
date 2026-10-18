using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class ActivitySettingsForm : Form
    {
        private readonly CheckBox _thinkingCheckBox;
        private readonly CheckBox _planCheckBox;
        private readonly CheckBox _toolCheckBox;
        private readonly CheckBox _commandCheckBox;
        private readonly CheckBox _fileCheckBox;
        private readonly CheckBox _noticeCheckBox;
        private readonly CheckBox _errorCheckBox;
        private readonly CheckBox _approvalCheckBox;
        private readonly CheckBox _accountCheckBox;
        private readonly CheckBox _protocolNoticeCheckBox;
        private readonly CheckBox _protocolWarningCheckBox;
        private readonly CheckBox _clearOnNewTurnCheckBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public ActivitySettingsForm(AppConfigSnapshot config)
        {
            config = config ?? new AppConfigSnapshot();

            Text = UiLocalization.Text("ActivitySettingsForm.Title", "Activity Messages");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(640, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _thinkingCheckBox = CreateCheckBox(
                UiLocalization.Text("ActivitySettingsForm.Check.Thinking", "Show thinking messages(&T)"),
                UiLocalization.Text("ActivitySettingsForm.Accessible.Thinking", "Show thinking messages"),
                0,
                config.ShowActivityThinkingMessages);
            _planCheckBox = CreateCheckBox(
                UiLocalization.Text("ActivitySettingsForm.Check.Plan", "Show plan messages(&P)"),
                UiLocalization.Text("ActivitySettingsForm.Accessible.Plan", "Show plan messages"),
                1,
                config.ShowActivityPlanMessages);
            _toolCheckBox = CreateCheckBox(
                UiLocalization.Text("ActivitySettingsForm.Check.Tool", "Show tool messages(&L)"),
                UiLocalization.Text("ActivitySettingsForm.Accessible.Tool", "Show tool messages"),
                2,
                config.ShowActivityToolMessages);
            _commandCheckBox = CreateCheckBox(
                UiLocalization.Text("ActivitySettingsForm.Check.Command", "Show command messages(&M)"),
                UiLocalization.Text("ActivitySettingsForm.Accessible.Command", "Show command messages"),
                3,
                config.ShowActivityCommandMessages);
            _fileCheckBox = CreateCheckBox(
                UiLocalization.Text("ActivitySettingsForm.Check.File", "Show file messages(&F)"),
                UiLocalization.Text("ActivitySettingsForm.Accessible.File", "Show file messages"),
                4,
                config.ShowActivityFileMessages);
            _noticeCheckBox = CreateCheckBox(
                UiLocalization.Text("ActivitySettingsForm.Check.Notice", "Show notice messages(&N)"),
                UiLocalization.Text("ActivitySettingsForm.Accessible.Notice", "Show notice messages"),
                5,
                config.ShowActivityNoticeMessages);
            _errorCheckBox = CreateCheckBox(
                UiLocalization.Text("ActivitySettingsForm.Check.Error", "Show error messages(&E)"),
                UiLocalization.Text("ActivitySettingsForm.Accessible.Error", "Show error messages"),
                6,
                config.ShowActivityErrorMessages);
            _approvalCheckBox = CreateCheckBox(
                UiLocalization.Text("ActivitySettingsForm.Check.Approval", "Show approval messages(&A)"),
                UiLocalization.Text("ActivitySettingsForm.Accessible.Approval", "Show approval messages"),
                7,
                config.ShowActivityApprovalMessages);
            _accountCheckBox = CreateCheckBox(
                UiLocalization.Text("ActivitySettingsForm.Check.Account", "Show account messages(&U)"),
                UiLocalization.Text("ActivitySettingsForm.Accessible.Account", "Show account messages"),
                8,
                config.ShowActivityAccountMessages);
            _protocolNoticeCheckBox = CreateCheckBox(
                UiLocalization.Text("ActivitySettingsForm.Check.ProtocolNotice", "Show protocol notices(&I)"),
                UiLocalization.Text("ActivitySettingsForm.Accessible.ProtocolNotice", "Show protocol notices"),
                9,
                config.ShowActivityProtocolNoticeMessages);
            _protocolWarningCheckBox = CreateCheckBox(
                UiLocalization.Text("ActivitySettingsForm.Check.ProtocolWarning", "Show protocol warnings(&W)"),
                UiLocalization.Text("ActivitySettingsForm.Accessible.ProtocolWarning", "Show protocol warnings"),
                10,
                config.ShowActivityProtocolWarningMessages);
            _clearOnNewTurnCheckBox = CreateCheckBox(
                UiLocalization.Text("ActivitySettingsForm.Check.ClearOnNewTurn", "Clear activity history when a new turn starts(&H)"),
                UiLocalization.Text("ActivitySettingsForm.Accessible.ClearOnNewTurn", "Clear activity history when a new turn starts"),
                11,
                config.ClearActivityOnNewTurn);

            _okButton = new Button
            {
                Text = UiLocalization.Text("Common.OK", "&OK"),
                AccessibleName = UiLocalization.Text("Common.Accessible.OK", "OK"),
                AutoSize = true,
                TabIndex = 12
            };
            UiTheme.ApplyDialogButton(_okButton, UiGlyph.Ok);
            _okButton.Click += OkButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("Common.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = 13,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            BuildLayout();
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }

        public bool ShowThinkingMessages { get; private set; }

        public bool ShowPlanMessages { get; private set; }

        public bool ShowToolMessages { get; private set; }

        public bool ShowCommandMessages { get; private set; }

        public bool ShowFileMessages { get; private set; }

        public bool ShowNoticeMessages { get; private set; }

        public bool ShowErrorMessages { get; private set; }

        public bool ShowApprovalMessages { get; private set; }

        public bool ShowAccountMessages { get; private set; }

        public bool ShowProtocolNoticeMessages { get; private set; }

        public bool ShowProtocolWarningMessages { get; private set; }

        public bool ClearOnNewTurn { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _thinkingCheckBox.Focus();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 14,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            for (var i = 0; i < 12; i++)
            {
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var buttonPanel = UiControlFactory.CreateDialogButtonBar();
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_okButton);

            root.Controls.Add(_thinkingCheckBox, 0, 0);
            root.Controls.Add(_planCheckBox, 0, 1);
            root.Controls.Add(_toolCheckBox, 0, 2);
            root.Controls.Add(_commandCheckBox, 0, 3);
            root.Controls.Add(_fileCheckBox, 0, 4);
            root.Controls.Add(_noticeCheckBox, 0, 5);
            root.Controls.Add(_errorCheckBox, 0, 6);
            root.Controls.Add(_approvalCheckBox, 0, 7);
            root.Controls.Add(_accountCheckBox, 0, 8);
            root.Controls.Add(_protocolNoticeCheckBox, 0, 9);
            root.Controls.Add(_protocolWarningCheckBox, 0, 10);
            root.Controls.Add(_clearOnNewTurnCheckBox, 0, 11);
            root.Controls.Add(buttonPanel, 0, 13);
            Controls.Add(root);
        }

        private static CheckBox CreateCheckBox(string text, string accessibleName, int tabIndex, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Text = text,
                AccessibleName = accessibleName,
                Margin = new Padding(0, 6, 0, 6),
                TabIndex = tabIndex,
                Checked = isChecked
            };
            UiTheme.ApplyCheckBox(checkBox);
            return checkBox;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            ShowThinkingMessages = _thinkingCheckBox.Checked;
            ShowPlanMessages = _planCheckBox.Checked;
            ShowToolMessages = _toolCheckBox.Checked;
            ShowCommandMessages = _commandCheckBox.Checked;
            ShowFileMessages = _fileCheckBox.Checked;
            ShowNoticeMessages = _noticeCheckBox.Checked;
            ShowErrorMessages = _errorCheckBox.Checked;
            ShowApprovalMessages = _approvalCheckBox.Checked;
            ShowAccountMessages = _accountCheckBox.Checked;
            ShowProtocolNoticeMessages = _protocolNoticeCheckBox.Checked;
            ShowProtocolWarningMessages = _protocolWarningCheckBox.Checked;
            ClearOnNewTurn = _clearOnNewTurnCheckBox.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
