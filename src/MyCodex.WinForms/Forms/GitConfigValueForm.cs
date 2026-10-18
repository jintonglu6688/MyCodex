using System.Drawing;
using System.Windows.Forms;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitConfigValueForm : Form
    {
        private readonly TextBox _valueTextBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public GitConfigValueForm(string title, string label, string accessibleName, string initialValue)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(540, 160);
            UiTheme.ApplyForm(this);

            _valueTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                Text = initialValue ?? string.Empty,
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_valueTextBox, false, false);

            _okButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitConfigValueForm.Button.Save", "Save(&S)"),
                UiLocalization.Text("GitConfigValueForm.Accessible.Save", "Save"),
                1,
                112);
            _cancelButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitConfigValueForm.Button.Cancel", "&Cancel"),
                UiLocalization.Text("GitConfigValueForm.Accessible.Cancel", "Cancel"),
                2,
                112);
            UiTheme.ApplyButton(_okButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _okButton.Click += (sender, e) => AcceptValue();
            _cancelButton.Click += (sender, e) => DialogResult = DialogResult.Cancel;

            BuildLayout(label);
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            Shown += (sender, e) =>
            {
                _valueTextBox.SelectAll();
                _valueTextBox.Focus();
            };
        }

        public string Value
        {
            get { return (_valueTextBox.Text ?? string.Empty).Trim(); }
        }

        private void AcceptValue()
        {
            if (string.IsNullOrWhiteSpace(Value))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitConfigValueForm.Message.ValueRequired", "Enter a value first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _valueTextBox.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
        }

        private void BuildLayout(string labelText)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var valueLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = labelText,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(valueLabel, true);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_cancelButton);
            footer.Controls.Add(_okButton);

            root.Controls.Add(valueLabel, 0, 0);
            root.Controls.Add(_valueTextBox, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
        }
    }
}
