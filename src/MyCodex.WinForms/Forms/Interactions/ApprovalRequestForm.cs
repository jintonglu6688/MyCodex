using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public class ApprovalRequestForm : Form
    {
        private readonly AccessibleStaticTextControl _summaryControl;
        private readonly TextBox _detailsTextBox;
        private readonly Button _allowOnceButton;
        private readonly Button _allowForSessionButton;
        private readonly Button _declineButton;

        public ApprovalRequestForm(AppServerEventArgs approvalRequest)
        {
            if (approvalRequest == null)
            {
                throw new ArgumentNullException(nameof(approvalRequest));
            }

            var summary = ApprovalRequestSummaryBuilder.Build(approvalRequest);

            Text = summary.WindowTitle;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 320);
            Size = new Size(760, 420);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _summaryControl = new AccessibleStaticTextControl
            {
                Dock = DockStyle.Fill,
                Text = summary.QuestionText,
                AccessibleName = summary.QuestionText,
                AccessibleRole = AccessibleRole.StaticText,
                BackColor = UiTheme.WindowBackColor,
                ForeColor = UiTheme.TextColor,
                TabIndex = 0,
                Margin = new Padding(0, 0, 0, UiTheme.ControlSpacing)
            };

            _detailsTextBox = CreateReadOnlyDetailsTextBox(
                summary.DetailsText,
                UiLocalization.Text("ApprovalRequestForm.Accessible.Details", "Details"));
            _detailsTextBox.Margin = new Padding(0, 0, 0, UiTheme.ControlSpacing);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                TabStop = false
            };

            _allowOnceButton = CreateDecisionButton(
                GetDecisionButtonText(summary.ActionKey, AppServerApprovalDecision.AllowOnce),
                GetDecisionAccessibleName(summary.ActionKey, AppServerApprovalDecision.AllowOnce),
                AppServerApprovalDecision.AllowOnce,
                DialogResult.OK,
                1,
                UiGlyph.Ok);
            buttonPanel.Controls.Add(_allowOnceButton);

            if (approvalRequest.CanApproveForSession)
            {
                _allowForSessionButton = CreateDecisionButton(
                    GetDecisionButtonText(summary.ActionKey, AppServerApprovalDecision.AllowForSession),
                    GetDecisionAccessibleName(summary.ActionKey, AppServerApprovalDecision.AllowForSession),
                    AppServerApprovalDecision.AllowForSession,
                    DialogResult.OK,
                    2,
                    UiGlyph.Ok);
                buttonPanel.Controls.Add(_allowForSessionButton);
            }

            _declineButton = CreateDecisionButton(
                GetDecisionButtonText(summary.ActionKey, AppServerApprovalDecision.Decline),
                GetDecisionAccessibleName(summary.ActionKey, AppServerApprovalDecision.Decline),
                AppServerApprovalDecision.Decline,
                DialogResult.Cancel,
                approvalRequest.CanApproveForSession ? 3 : 2,
                UiGlyph.Cancel);
            buttonPanel.Controls.Add(_declineButton);

            root.Controls.Add(_summaryControl, 0, 0);
            root.Controls.Add(_detailsTextBox, 0, 1);
            root.Controls.Add(buttonPanel, 0, 2);

            Controls.Add(root);

            AcceptButton = _allowOnceButton;
            CancelButton = _declineButton;
        }

        public AppServerApprovalDecision SelectedDecision { get; private set; } = AppServerApprovalDecision.Decline;

        public bool WasExternallyResolved { get; private set; }

        public void CloseAsExternallyResolved()
        {
            WasExternallyResolved = true;
            DialogResult = DialogResult.Abort;
            Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _summaryControl.Focus();
        }

        private static TextBox CreateReadOnlyDetailsTextBox(string text, string accessibleName)
        {
            var textBox = new ThemedTextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                TabStop = false,
                WordWrap = true,
                Text = text ?? string.Empty,
                AccessibleName = accessibleName,
                AccessibleRole = AccessibleRole.Text
            };
            UiTheme.ApplyTextBox(textBox, false, true);
            return textBox;
        }

        private Button CreateDecisionButton(
            string text,
            string accessibleName,
            AppServerApprovalDecision decision,
            DialogResult dialogResult,
            int tabIndex,
            UiGlyph glyph)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                TabIndex = tabIndex,
                DialogResult = dialogResult,
                Margin = new Padding(0, 0, UiTheme.ControlSpacing, 0)
            };
            UiTheme.ApplyDialogButton(button, glyph);
            button.Click += delegate
            {
                SelectedDecision = decision;
                DialogResult = dialogResult;
                Close();
            };

            return button;
        }

        private static string GetDecisionButtonText(ApprovalRequestActionKey actionKey, AppServerApprovalDecision decision)
        {
            switch (decision)
            {
                case AppServerApprovalDecision.Decline:
                    return GetActionText(
                        actionKey,
                        "ApprovalRequestForm.Button.Decline.Delete",
                        "Decline delete(&D)",
                        "ApprovalRequestForm.Button.Decline.Write",
                        "Decline write(&D)",
                        "ApprovalRequestForm.Button.Decline.Modify",
                        "Decline modify(&D)",
                        "ApprovalRequestForm.Button.Decline.Move",
                        "Decline move(&D)",
                        "ApprovalRequestForm.Button.Decline.Change",
                        "Decline change(&D)",
                        "ApprovalRequestForm.Button.Decline.Generic",
                        "Decline(&D)");
                case AppServerApprovalDecision.AllowForSession:
                    return GetActionText(
                        actionKey,
                        "ApprovalRequestForm.Button.AllowForSession.Delete",
                        "Allow delete for session(&S)",
                        "ApprovalRequestForm.Button.AllowForSession.Write",
                        "Allow write for session(&S)",
                        "ApprovalRequestForm.Button.AllowForSession.Modify",
                        "Allow modify for session(&S)",
                        "ApprovalRequestForm.Button.AllowForSession.Move",
                        "Allow move for session(&S)",
                        "ApprovalRequestForm.Button.AllowForSession.Change",
                        "Allow change for session(&S)",
                        "ApprovalRequestForm.Button.AllowForSession.Generic",
                        "Allow for Session(&S)");
                default:
                    return GetActionText(
                        actionKey,
                        "ApprovalRequestForm.Button.AllowOnce.Delete",
                        "Allow delete(&O)",
                        "ApprovalRequestForm.Button.AllowOnce.Write",
                        "Allow write(&O)",
                        "ApprovalRequestForm.Button.AllowOnce.Modify",
                        "Allow modify(&O)",
                        "ApprovalRequestForm.Button.AllowOnce.Move",
                        "Allow move(&O)",
                        "ApprovalRequestForm.Button.AllowOnce.Change",
                        "Allow change(&O)",
                        "ApprovalRequestForm.Button.AllowOnce.Generic",
                        "Allow once(&O)");
            }
        }

        private static string GetDecisionAccessibleName(ApprovalRequestActionKey actionKey, AppServerApprovalDecision decision)
        {
            switch (decision)
            {
                case AppServerApprovalDecision.Decline:
                    return GetActionText(
                        actionKey,
                        "ApprovalRequestForm.Accessible.Decline.Delete",
                        "Decline delete",
                        "ApprovalRequestForm.Accessible.Decline.Write",
                        "Decline write",
                        "ApprovalRequestForm.Accessible.Decline.Modify",
                        "Decline modify",
                        "ApprovalRequestForm.Accessible.Decline.Move",
                        "Decline move",
                        "ApprovalRequestForm.Accessible.Decline.Change",
                        "Decline change",
                        "ApprovalRequestForm.Accessible.Decline.Generic",
                        "Decline");
                case AppServerApprovalDecision.AllowForSession:
                    return GetActionText(
                        actionKey,
                        "ApprovalRequestForm.Accessible.AllowForSession.Delete",
                        "Allow delete for this session",
                        "ApprovalRequestForm.Accessible.AllowForSession.Write",
                        "Allow write for this session",
                        "ApprovalRequestForm.Accessible.AllowForSession.Modify",
                        "Allow modify for this session",
                        "ApprovalRequestForm.Accessible.AllowForSession.Move",
                        "Allow move for this session",
                        "ApprovalRequestForm.Accessible.AllowForSession.Change",
                        "Allow change for this session",
                        "ApprovalRequestForm.Accessible.AllowForSession.Generic",
                        "Allow for this session");
                default:
                    return GetActionText(
                        actionKey,
                        "ApprovalRequestForm.Accessible.AllowOnce.Delete",
                        "Allow delete once",
                        "ApprovalRequestForm.Accessible.AllowOnce.Write",
                        "Allow write once",
                        "ApprovalRequestForm.Accessible.AllowOnce.Modify",
                        "Allow modify once",
                        "ApprovalRequestForm.Accessible.AllowOnce.Move",
                        "Allow move once",
                        "ApprovalRequestForm.Accessible.AllowOnce.Change",
                        "Allow change once",
                        "ApprovalRequestForm.Accessible.AllowOnce.Generic",
                        "Allow once");
            }
        }

        private static string GetActionText(
            ApprovalRequestActionKey actionKey,
            string deleteKey,
            string deleteFallback,
            string writeKey,
            string writeFallback,
            string modifyKey,
            string modifyFallback,
            string moveKey,
            string moveFallback,
            string changeKey,
            string changeFallback,
            string genericKey,
            string genericFallback)
        {
            switch (actionKey)
            {
                case ApprovalRequestActionKey.Delete:
                    return UiLocalization.Text(deleteKey, deleteFallback);
                case ApprovalRequestActionKey.Write:
                    return UiLocalization.Text(writeKey, writeFallback);
                case ApprovalRequestActionKey.Modify:
                    return UiLocalization.Text(modifyKey, modifyFallback);
                case ApprovalRequestActionKey.Move:
                    return UiLocalization.Text(moveKey, moveFallback);
                case ApprovalRequestActionKey.Change:
                    return UiLocalization.Text(changeKey, changeFallback);
                default:
                    return UiLocalization.Text(genericKey, genericFallback);
            }
        }

    }
}
