using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class MessageSearchForm : Form
    {
        private readonly TextBox _keywordTextBox;
        private readonly Button _findNextButton;
        private readonly Button _findPreviousButton;
        private readonly Button _cancelButton;

        public MessageSearchForm(string initialKeyword)
        {
            Text = UiLocalization.Text("MessageSearchForm.Title", "Find Messages");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(580, 230);
            UiTheme.ApplyForm(this);

            var titleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                Text = UiLocalization.Text("MessageSearchForm.Header", "Find in current message list"),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var tipLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 54,
                Text = UiLocalization.Text(
                    "MessageSearchForm.Tip",
                    "Enter a keyword. Press Enter for next match, Shift+Enter for previous match. Search only locates within the current list."),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };

            var formTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                TabStop = false
            };
            formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
            formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var keywordLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("MessageSearchForm.KeywordLabel", "Keyword"),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            _keywordTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("MessageSearchForm.Accessible.Keyword", "Keyword"),
                Text = initialKeyword ?? string.Empty
            };
            UiTheme.ApplyTextBox(_keywordTextBox, false, false);
            _keywordTextBox.KeyDown += KeywordTextBox_KeyDown;

            formTable.Controls.Add(keywordLabel, 0, 0);
            formTable.Controls.Add(_keywordTextBox, 1, 0);

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(0, 12, 0, 0),
                TabStop = false
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 336,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            _findNextButton = new Button
            {
                Text = UiLocalization.Text("MessageSearchForm.FindNext", "Find &Next"),
                AccessibleName = UiLocalization.Text("MessageSearchForm.Accessible.FindNext", "Find Next"),
                Width = 96,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                UseMnemonic = true
            };
            UiTheme.ApplyButton(_findNextButton, UiGlyph.Search);
            _findNextButton.Click += FindNextButton_Click;

            _findPreviousButton = new Button
            {
                Text = UiLocalization.Text("MessageSearchForm.FindPrevious", "Find &Previous"),
                AccessibleName = UiLocalization.Text("MessageSearchForm.Accessible.FindPrevious", "Find Previous"),
                Width = 96,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                UseMnemonic = true
            };
            UiTheme.ApplyButton(_findPreviousButton, UiGlyph.Search);
            _findPreviousButton.Click += FindPreviousButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("MessageSearchForm.Close", "&Close"),
                AccessibleName = UiLocalization.Text("MessageSearchForm.Accessible.Close", "Close"),
                Width = 96,
                Height = 32,
                UseMnemonic = true,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            buttonPanel.Controls.Add(_findNextButton);
            buttonPanel.Controls.Add(_findPreviousButton);
            buttonPanel.Controls.Add(_cancelButton);
            footerPanel.Controls.Add(buttonPanel);

            contentPanel.Controls.Add(formTable);
            Controls.Add(contentPanel);
            Controls.Add(footerPanel);
            Controls.Add(tipLabel);
            Controls.Add(titleLabel);

            AcceptButton = _findNextButton;
            CancelButton = _cancelButton;
            Shown += MessageSearchForm_Shown;
        }

        public string SearchText
        {
            get { return (_keywordTextBox.Text ?? string.Empty).Trim(); }
        }

        public bool SearchForward { get; private set; } = true;

        private void MessageSearchForm_Shown(object sender, EventArgs e)
        {
            _keywordTextBox.Focus();
            _keywordTextBox.SelectAll();
        }

        private void KeywordTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Shift)
            {
                Submit(false);
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void FindNextButton_Click(object sender, EventArgs e)
        {
            Submit(true);
        }

        private void FindPreviousButton_Click(object sender, EventArgs e)
        {
            Submit(false);
        }

        private void Submit(bool forward)
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MessageSearchForm.EmptyKeyword", "Please enter a keyword to search."),
                    UiLocalization.Text("MessageSearchForm.Title", "Find Messages"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _keywordTextBox.Focus();
                _keywordTextBox.SelectAll();
                return;
            }

            SearchForward = forward;
            DialogResult = DialogResult.OK;
        }
    }
}
