using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class AppServerFilePreviewGoToLineDialog : Form
    {
        private readonly AppServerFilePreviewForm _ownerForm;
        private readonly NumericUpDown _lineNumberBox;
        private readonly Button _goToLineButton;
        private readonly Button _closeButton;
        private readonly Control _initialFocusControl;

        internal AppServerFilePreviewGoToLineDialog(AppServerFilePreviewForm ownerForm)
        {
            _ownerForm = ownerForm ?? throw new ArgumentNullException(nameof(ownerForm));
            UiTheme.ApplyForm(this);
            Text = UiLocalization.Text("AppServerFilePreviewForm.GoToLineDialog.Title", "Go to line");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(320, 92);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowOnly;
            BackColor = UiTheme.WindowBackColor;

            var lineCount = Math.Max(1, _ownerForm.GetLineCountForDialog());
            _lineNumberBox = new NumericUpDown
            {
                AccessibleName = UiLocalization.Text("AppServerFilePreviewForm.Accessible.LineNumber", "Line number"),
                Minimum = 1,
                Maximum = lineCount,
                Value = Math.Min(Math.Max(1, _ownerForm.CurrentLineForTests), lineCount),
                Width = 120,
                TabIndex = 0
            };
            _goToLineButton = CreateButton(
                UiLocalization.Text("AppServerFilePreviewForm.Button.GoToLine", "Go To(&G)"),
                UiLocalization.Text("AppServerFilePreviewForm.Accessible.GoToLine", "Go to line"),
                1,
                UiGlyph.Ok);
            _goToLineButton.Click += (sender, e) => _ownerForm.GoToLineFromDialog((int)_lineNumberBox.Value);
            _closeButton = CreateButton(
                UiLocalization.Text("AppServerFilePreviewForm.Button.Close", "Close(&C)"),
                UiLocalization.Text("AppServerFilePreviewForm.Accessible.Close", "Close"),
                2,
                UiGlyph.Cancel);
            _closeButton.Click += (sender, e) => Close();
            AcceptButton = _goToLineButton;
            CancelButton = _closeButton;
            _initialFocusControl = _lineNumberBox;

            ConfigureLayout();
            Shown += (sender, e) => _initialFocusControl.Focus();
        }

        internal string LineNumberAccessibleNameForTests
        {
            get { return _lineNumberBox.AccessibleName; }
        }

        internal string InitialFocusAccessibleNameForTests
        {
            get { return _initialFocusControl.AccessibleName; }
        }

        private void ConfigureLayout()
        {
            var root = new TableLayoutPanel
            {
                AutoSize = true,
                BackColor = UiTheme.PanelBackColor,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(UiTheme.OuterPadding),
                RowCount = 2,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var inputRow = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                TabStop = false,
                WrapContents = false
            };
            inputRow.Controls.Add(new Label
            {
                AutoSize = false,
                Margin = new Padding(0, 3, 0, 0),
                Size = new Size(72, UiTheme.ButtonHeight),
                TabStop = false,
                Text = UiLocalization.Text("AppServerFilePreviewForm.Label.Line", "Line"),
                TextAlign = ContentAlignment.MiddleLeft
            });
            inputRow.Controls.Add(_lineNumberBox);

            var buttonRow = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                TabStop = false,
                WrapContents = false
            };
            buttonRow.Controls.Add(_goToLineButton);
            buttonRow.Controls.Add(_closeButton);

            root.Controls.Add(inputRow, 0, 0);
            root.Controls.Add(buttonRow, 0, 1);
            Controls.Add(root);
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex, UiGlyph glyph)
        {
            var button = new Button
            {
                AccessibleName = accessibleName,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(UiTheme.ControlSpacing, 0, 0, 0),
                TabIndex = tabIndex,
                Text = text,
                UseMnemonic = true,
                Width = UiTheme.ButtonWidth
            };
            UiTheme.ApplyDialogButton(button, glyph);
            return button;
        }
    }
}
