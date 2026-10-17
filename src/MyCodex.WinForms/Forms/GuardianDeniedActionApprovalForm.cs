using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GuardianDeniedActionApprovalForm : Form
    {
        private readonly AccessibleStaticTextControl _summaryControl;
        private readonly TextBox _detailsTextBox;
        private readonly Button _approveButton;
        private readonly Button _cancelButton;

        public GuardianDeniedActionApprovalForm(AppServerGuardianDeniedActionApprovalRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            Text = UiLocalization.Text("GuardianDeniedActionApprovalForm.Title", "Approve Denied Action");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 480);
            Size = new Size(780, 560);
            MinimizeBox = false;
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);

            var summaryText = UiLocalization.Text(
                "GuardianDeniedActionApprovalForm.Heading",
                "Automatic review denied this action. Approve it manually?") +
                Environment.NewLine + BuildDetailsText(request, false);
            _summaryControl = new AccessibleStaticTextControl
            {
                Dock = DockStyle.Top,
                Text = summaryText,
                AccessibleName = summaryText,
                AccessibleRole = AccessibleRole.StaticText,
                TabIndex = 0,
                BackColor = UiTheme.WindowBackColor,
                ForeColor = UiTheme.TextColor
            };
            var summaryPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabStop = false };
            summaryPanel.Controls.Add(_summaryControl);
            summaryPanel.SizeChanged += delegate
            {
                _summaryControl.Height = _summaryControl.GetPreferredSize(
                    new Size(System.Math.Max(1, summaryPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth), 0)).Height;
            };

            _detailsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = UiTheme.CreateCodeFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                AccessibleName = UiLocalization.Text(
                    "GuardianDeniedActionApprovalForm.Accessible.Details",
                    "Denied action details"),
                TabStop = false,
                Text = BuildDetailsText(request)
            };
            UiTheme.ApplyTextBox(_detailsTextBox, true, true);

            _approveButton = new Button
            {
                Text = UiLocalization.Text("GuardianDeniedActionApprovalForm.Button.Approve", "Approve(&P)"),
                AccessibleName = UiLocalization.Text(
                    "GuardianDeniedActionApprovalForm.Accessible.Approve",
                    "Approve denied action"),
                AutoSize = true,
                DialogResult = DialogResult.OK,
                TabIndex = 1
            };
            UiTheme.ApplyDialogButton(_approveButton, UiGlyph.Approval);

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("Common.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                DialogResult = DialogResult.Cancel,
                TabIndex = 2
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 0),
                TabStop = false
            };
            buttonPanel.Controls.Add(_approveButton);
            buttonPanel.Controls.Add(_cancelButton);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(summaryPanel, 0, 0);
            root.Controls.Add(_detailsTextBox, 0, 1);
            root.Controls.Add(buttonPanel, 0, 2);

            Controls.Add(root);
            AcceptButton = _cancelButton;
            CancelButton = _cancelButton;
        }

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

        private static string BuildDetailsText(AppServerGuardianDeniedActionApprovalRequest request, bool includeEvent = true)
        {
            var builder = new StringBuilder();
            AppendLine(builder, UiLocalization.Text("GuardianDeniedActionApprovalForm.Detail.ThreadId", "Thread ID"), request.ThreadId);
            AppendLine(builder, UiLocalization.Text("GuardianDeniedActionApprovalForm.Detail.RiskLevel", "Risk level"), request.RiskLevel);
            AppendLine(builder, UiLocalization.Text("GuardianDeniedActionApprovalForm.Detail.Rationale", "Rationale"), request.Rationale);

            if (request.Action != null)
            {
                AppendLine(builder, UiLocalization.Text("GuardianDeniedActionApprovalForm.Detail.ActionType", "Action type"), request.Action.ActionType);
                AppendLine(builder, UiLocalization.Text("GuardianDeniedActionApprovalForm.Detail.ActionTitle", "Action title"), request.Action.Title);
                AppendLine(builder, UiLocalization.Text("GuardianDeniedActionApprovalForm.Detail.ActionDetails", "Action details"), request.Action.Details);
            }

            if (includeEvent && request.Event != null && request.Event.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("GuardianDeniedActionApprovalForm.Detail.Event", "Guardian assessment event"));
                builder.AppendLine(SerializeEvent(request.Event));
            }

            return builder.ToString().TrimEnd();
        }

        private static void AppendLine(StringBuilder builder, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            builder.Append(label);
            builder.Append(": ");
            builder.AppendLine(value.Trim());
        }

        private static string SerializeEvent(IDictionary<string, object> value)
        {
            try
            {
                return new JavaScriptSerializer().Serialize(value);
            }
            catch
            {
                return UiLocalization.Text("GuardianDeniedActionApprovalForm.Value.EventUnavailable", "(Unable to display event JSON)");
            }
        }
    }
}
