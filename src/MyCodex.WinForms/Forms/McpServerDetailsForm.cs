using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class McpServerDetailsForm : Form
    {
        private readonly TextBox _detailTextBox;
        private readonly Button _closeButton;

        public McpServerDetailsForm(string title, string headerText, string detailText)
        {
            Text = string.IsNullOrWhiteSpace(title)
                ? UiLocalization.Text("McpServerDetailsForm.Title", "MCP Server Details")
                : title;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(840, 620);
            Size = new Size(980, 760);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            var headerLabel = new Label
            {
                Text = string.IsNullOrWhiteSpace(headerText)
                    ? UiLocalization.Text("McpServerDetailsForm.Header", "MCP Server Details")
                    : headerText,
                Dock = DockStyle.Fill,
                Height = 34,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            _detailTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                AccessibleName = UiLocalization.Text("McpServerDetailsForm.Accessible.Details", "MCP Server Details"),
                TabIndex = 0,
                Text = detailText ?? string.Empty
            };
            UiTheme.ApplyTextBox(_detailTextBox, true, true);

            _closeButton = new Button
            {
                Text = UiLocalization.Text("McpServerDetailsForm.Button.Close", "&Close"),
                AutoSize = true,
                MinimumSize = new Size(108, 0),
                AccessibleName = UiLocalization.Text("McpServerDetailsForm.Accessible.Close", "Close"),
                TabIndex = 1
            };
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            _closeButton.Click += CloseButton_Click;

            var actionPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0),
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            actionPanel.Controls.Add(_closeButton);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(headerLabel, 0, 0);
            root.Controls.Add(_detailTextBox, 0, 1);
            root.Controls.Add(actionPanel, 0, 2);

            Controls.Add(root);
            CancelButton = _closeButton;
            Shown += McpServerDetailsForm_Shown;
        }

        private void McpServerDetailsForm_Shown(object sender, EventArgs e)
        {
            _detailTextBox.Focus();
            TextBoxCaretHelper.MoveToStart(_detailTextBox);
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
