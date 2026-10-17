using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class AppServerFilePreviewFindReplaceDialog : Form
    {
        private readonly AppServerFilePreviewForm _ownerForm;
        private readonly TextBox _findTextBox;
        private readonly TextBox _replaceTextBox;
        private readonly Button _findPreviousButton;
        private readonly Button _findNextButton;
        private readonly Button _replaceButton;
        private readonly Button _replaceAllButton;
        private readonly Button _closeButton;
        private readonly Control _initialFocusControl;
        private readonly bool _replaceMode;

        internal AppServerFilePreviewFindReplaceDialog(AppServerFilePreviewForm ownerForm, bool replaceMode)
        {
            _ownerForm = ownerForm ?? throw new ArgumentNullException(nameof(ownerForm));
            UiTheme.ApplyForm(this);
            _replaceMode = replaceMode;
            Text = replaceMode
                ? UiLocalization.Text("AppServerFilePreviewForm.FindReplaceDialog.Title.Replace", "Replace")
                : UiLocalization.Text("AppServerFilePreviewForm.FindReplaceDialog.Title.Find", "Find");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = replaceMode ? new Size(520, 132) : new Size(520, 92);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowOnly;
            BackColor = UiTheme.WindowBackColor;

            _findTextBox = CreateTextBox(
                UiLocalization.Text("AppServerFilePreviewForm.Accessible.Find", "Find"),
                0,
                _ownerForm.FindTextForTests);
            _replaceTextBox = CreateTextBox(
                UiLocalization.Text("AppServerFilePreviewForm.Accessible.Replace", "Replace with"),
                1,
                _ownerForm.ReplaceTextForTests);
            _findPreviousButton = CreateButton(
                UiLocalization.Text("AppServerFilePreviewForm.Button.FindPrevious", "Previous(&P)"),
                UiLocalization.Text("AppServerFilePreviewForm.Accessible.FindPrevious", "Find previous"),
                2,
                UiGlyph.Search);
            _findPreviousButton.Click += (sender, e) => _ownerForm.FindFromDialog(_findTextBox.Text, false);
            _findNextButton = CreateButton(
                UiLocalization.Text("AppServerFilePreviewForm.Button.FindNext", "Next(&N)"),
                UiLocalization.Text("AppServerFilePreviewForm.Accessible.FindNext", "Find next"),
                3,
                UiGlyph.Search);
            _findNextButton.Click += (sender, e) => _ownerForm.FindFromDialog(_findTextBox.Text, true);
            _replaceButton = CreateButton(
                UiLocalization.Text("AppServerFilePreviewForm.Button.Replace", "Replace(&E)"),
                UiLocalization.Text("AppServerFilePreviewForm.Accessible.ReplaceCurrent", "Replace current"),
                4,
                UiGlyph.FileChange);
            _replaceButton.Click += (sender, e) => _ownerForm.ReplaceCurrentFromDialog(_findTextBox.Text, _replaceTextBox.Text);
            _replaceAllButton = CreateButton(
                UiLocalization.Text("AppServerFilePreviewForm.Button.ReplaceAll", "Replace All(&A)"),
                UiLocalization.Text("AppServerFilePreviewForm.Accessible.ReplaceAll", "Replace all"),
                5,
                UiGlyph.FileChange);
            _replaceAllButton.Click += (sender, e) => _ownerForm.ReplaceAllFromDialog(_findTextBox.Text, _replaceTextBox.Text);
            _closeButton = CreateButton(
                UiLocalization.Text("AppServerFilePreviewForm.Button.Close", "Close(&C)"),
                UiLocalization.Text("AppServerFilePreviewForm.Accessible.Close", "Close"),
                replaceMode ? 6 : 4,
                UiGlyph.Cancel);
            _closeButton.Click += (sender, e) => Close();
            AcceptButton = _findNextButton;
            CancelButton = _closeButton;
            _initialFocusControl = _findTextBox;

            ConfigureLayout(replaceMode);
            Shown += (sender, e) => _initialFocusControl.Focus();
        }

        internal string[] FocusOrderAccessibleNamesForTests
        {
            get
            {
                if (_replaceMode)
                {
                    return new[]
                    {
                        _findTextBox.AccessibleName,
                        _replaceTextBox.AccessibleName,
                        _findPreviousButton.AccessibleName,
                        _findNextButton.AccessibleName,
                        _replaceButton.AccessibleName,
                        _replaceAllButton.AccessibleName,
                        _closeButton.AccessibleName
                    };
                }

                return new[]
                {
                    _findTextBox.AccessibleName,
                    _findPreviousButton.AccessibleName,
                    _findNextButton.AccessibleName,
                    _closeButton.AccessibleName
                };
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F3)
            {
                _ownerForm.FindFromDialog(_findTextBox.Text, true);
                return true;
            }

            if (keyData == (Keys.Shift | Keys.F3))
            {
                _ownerForm.FindFromDialog(_findTextBox.Text, false);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ConfigureLayout(bool replaceMode)
        {
            var panel = new TableLayoutPanel
            {
                AutoSize = true,
                BackColor = UiTheme.PanelBackColor,
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Padding = new Padding(UiTheme.OuterPadding),
                RowCount = replaceMode ? 3 : 2,
                TabStop = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            if (replaceMode)
            {
                panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            }

            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(CreateDisplayLabel(UiLocalization.Text("AppServerFilePreviewForm.Label.Find", "Find")), 0, 0);
            panel.Controls.Add(_findTextBox, 1, 0);
            _replaceTextBox.Visible = replaceMode;
            _replaceButton.Visible = replaceMode;
            _replaceAllButton.Visible = replaceMode;
            if (replaceMode)
            {
                panel.Controls.Add(CreateDisplayLabel(UiLocalization.Text("AppServerFilePreviewForm.Label.Replace", "Replace")), 0, 1);
                panel.Controls.Add(_replaceTextBox, 1, 1);
            }

            var buttonRow = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                TabStop = false,
                WrapContents = false
            };
            buttonRow.Controls.Add(_findPreviousButton);
            buttonRow.Controls.Add(_findNextButton);
            if (replaceMode)
            {
                buttonRow.Controls.Add(_replaceButton);
                buttonRow.Controls.Add(_replaceAllButton);
            }

            buttonRow.Controls.Add(_closeButton);
            panel.SetColumnSpan(buttonRow, 2);
            panel.Controls.Add(buttonRow, 0, replaceMode ? 2 : 1);
            Controls.Add(panel);
        }

        private static Label CreateDisplayLabel(string text)
        {
            return new Label
            {
                AutoSize = false,
                Margin = new Padding(0, 3, 0, 0),
                Size = new Size(58, UiTheme.ButtonHeight),
                TabStop = false,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static TextBox CreateTextBox(string accessibleName, int tabIndex, string text)
        {
            var textBox = new TextBox
            {
                AccessibleName = accessibleName,
                Margin = new Padding(UiTheme.ControlSpacing, 0, 0, 0),
                TabIndex = tabIndex,
                Text = text ?? string.Empty,
                Width = 210
            };
            UiTheme.ApplyTextBox(textBox, false, false);
            return textBox;
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
