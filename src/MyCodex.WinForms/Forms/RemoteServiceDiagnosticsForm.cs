using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class RemoteServiceDiagnosticsForm : Form
    {
        private readonly TextBox _diagnosticsTextBox;
        private readonly Button _closeButton;

        public RemoteServiceDiagnosticsForm(string diagnosticsText)
        {
            Text = UiLocalization.Text("RemoteServiceDiagnosticsForm.Title", "Remote Service Diagnostics");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 420);
            Size = new Size(760, 520);
            ShowInTaskbar = false;
            TabStop = false;
            UiTheme.ApplyForm(this);

            _diagnosticsTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("RemoteServiceDiagnosticsForm.Accessible.Diagnostics", "Diagnostics"),
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                TabIndex = 0,
                Text = diagnosticsText ?? string.Empty,
                WordWrap = false
            };
            UiTheme.ApplyTextBox(_diagnosticsTextBox, true, true);

            _closeButton = new Button
            {
                Text = UiLocalization.Text("RemoteServiceDiagnosticsForm.Button.Close", "C&lose"),
                AccessibleName = UiLocalization.Text("RemoteServiceDiagnosticsForm.Accessible.Close", "Close"),
                AutoSize = true,
                TabIndex = 1
            };
            UiTheme.ApplyDialogButton(_closeButton, UiGlyph.Cancel);
            _closeButton.Click += (sender, e) => Close();

            BuildLayout();
            CancelButton = _closeButton;
        }

        internal string DiagnosticsTextForTests
        {
            get { return _diagnosticsTextBox.Text; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _diagnosticsTextBox.Focus();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var buttons = UiControlFactory.CreateDialogButtonBar();
            buttons.Controls.Add(_closeButton);

            root.Controls.Add(_diagnosticsTextBox, 0, 0);
            root.Controls.Add(buttons, 0, 1);
            Controls.Add(root);
        }
    }
}
