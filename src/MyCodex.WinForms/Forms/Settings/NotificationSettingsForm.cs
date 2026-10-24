using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public class NotificationSettingsForm : Form
    {
        private readonly CheckBox _turnCompletedSoundCheckBox;
        private readonly CheckBox _subAgentTurnCompletedSoundCheckBox;
        private readonly CheckBox _approvalRequestSoundCheckBox;
        private readonly CheckBox _sendMessageSoundCheckBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public NotificationSettingsForm(NotificationPromptMode turnCompletedMode, NotificationPromptMode approvalRequestMode)
            : this(turnCompletedMode, NotificationPromptMode.Sound, approvalRequestMode, true, true)
        {
        }

        internal NotificationSettingsForm(
            NotificationPromptMode turnCompletedMode,
            NotificationPromptMode approvalRequestMode,
            bool sendMessageSoundEnabled,
            bool showDialogButtons)
            : this(turnCompletedMode, NotificationPromptMode.Sound, approvalRequestMode, sendMessageSoundEnabled, showDialogButtons)
        {
        }

        internal NotificationSettingsForm(
            NotificationPromptMode turnCompletedMode,
            NotificationPromptMode subAgentTurnCompletedMode,
            NotificationPromptMode approvalRequestMode,
            bool sendMessageSoundEnabled,
            bool showDialogButtons)
        {
            Text = UiLocalization.Text("NotificationSettingsForm.Title", "Sound");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(620, 320);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _turnCompletedSoundCheckBox = CreateSoundCheckBox(
                UiLocalization.Text("NotificationSettingsForm.Check.TurnCompletedSound", "Play sound when a turn completes or fails(&T)"),
                UiLocalization.Text("NotificationSettingsForm.Accessible.TurnCompletedSound", "Play sound when a turn completes or fails"),
                0);
            _subAgentTurnCompletedSoundCheckBox = CreateSoundCheckBox(
                UiLocalization.Text("NotificationSettingsForm.Check.SubAgentTurnCompletedSound", "Play sound when a subagent completes or fails(&G)"),
                UiLocalization.Text("NotificationSettingsForm.Accessible.SubAgentTurnCompletedSound", "Play sound when a subagent completes or fails"),
                1);
            _approvalRequestSoundCheckBox = CreateSoundCheckBox(
                UiLocalization.Text("NotificationSettingsForm.Check.ApprovalRequestSound", "Play sound when an approval request appears(&A)"),
                UiLocalization.Text("NotificationSettingsForm.Accessible.ApprovalRequestSound", "Play sound when an approval request appears"),
                2);
            _sendMessageSoundCheckBox = CreateSoundCheckBox(
                UiLocalization.Text("NotificationSettingsForm.Check.SendMessageSound", "Play sound after sending a message(&S)"),
                UiLocalization.Text("NotificationSettingsForm.Accessible.SendMessageSound", "Play sound after sending a message"),
                3);

            _okButton = new Button
            {
                Text = UiLocalization.Text("Common.OK", "&OK"),
                AccessibleName = UiLocalization.Text("Common.Accessible.OK", "OK"),
                AutoSize = true,
                TabIndex = 4
            };
            UiTheme.ApplyDialogButton(_okButton, UiGlyph.Ok);
            _okButton.Click += OkButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("Common.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = 5,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                TabStop = false,
                Visible = showDialogButtons
            };
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_okButton);

            root.Controls.Add(_turnCompletedSoundCheckBox, 0, 0);
            root.Controls.Add(_subAgentTurnCompletedSoundCheckBox, 0, 1);
            root.Controls.Add(_approvalRequestSoundCheckBox, 0, 2);
            root.Controls.Add(_sendMessageSoundCheckBox, 0, 3);
            root.Controls.Add(buttonPanel, 0, 4);

            Controls.Add(root);
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            _turnCompletedSoundCheckBox.Checked = IsSoundEnabled(turnCompletedMode);
            _subAgentTurnCompletedSoundCheckBox.Checked = IsSoundEnabled(subAgentTurnCompletedMode);
            _approvalRequestSoundCheckBox.Checked = IsSoundEnabled(approvalRequestMode);
            _sendMessageSoundCheckBox.Checked = sendMessageSoundEnabled;
        }

        public NotificationPromptMode TurnCompletedMode { get; private set; }

        public NotificationPromptMode SubAgentTurnCompletedMode { get; private set; }

        public NotificationPromptMode ApprovalRequestMode { get; private set; }

        public bool SendMessageSoundEnabled { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _turnCompletedSoundCheckBox.Focus();
        }

        private static CheckBox CreateSoundCheckBox(string text, string accessibleName, int tabIndex)
        {
            var checkBox = new CheckBox
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Text = text,
                AccessibleName = accessibleName,
                Margin = new Padding(0, 6, 0, 6),
                TabIndex = tabIndex
            };
            UiTheme.ApplyCheckBox(checkBox);
            return checkBox;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            ApplySelection();
            DialogResult = DialogResult.OK;
            Close();
        }

        internal bool ApplySelection(IWin32Window owner)
        {
            ApplySelection();
            return true;
        }

        private void ApplySelection()
        {
            TurnCompletedMode = ToMode(_turnCompletedSoundCheckBox.Checked);
            SubAgentTurnCompletedMode = ToMode(_subAgentTurnCompletedSoundCheckBox.Checked);
            ApprovalRequestMode = ToMode(_approvalRequestSoundCheckBox.Checked);
            SendMessageSoundEnabled = _sendMessageSoundCheckBox.Checked;
        }

        private static NotificationPromptMode ToMode(bool enabled)
        {
            return enabled ? NotificationPromptMode.Sound : NotificationPromptMode.Off;
        }

        private static bool IsSoundEnabled(NotificationPromptMode mode)
        {
            return mode != NotificationPromptMode.Off;
        }
    }
}
