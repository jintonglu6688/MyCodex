using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitBranchIntegrateForm : Form
    {
        private readonly TextBox _currentBranchTextBox;
        private readonly TextBox _targetBranchTextBox;
        private readonly TextBox _comparisonTextBox;
        private readonly ComboBox _modeComboBox;
        private readonly CheckBox _fetchFirstCheckBox;
        private readonly Button _startButton;
        private readonly Button _cancelButton;

        public GitBranchIntegrateForm(string currentBranch, string targetBranch, GitBranchComparison comparison)
        {
            Text = UiLocalization.Text("GitBranchIntegrateForm.Title", "Branch Integration");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(540, 340);
            Size = new Size(620, 400);
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _currentBranchTextBox = CreateReadOnlyTextBox(
                UiLocalization.Text("GitBranchIntegrateForm.Accessible.CurrentBranch", "Current branch"),
                currentBranch);
            _targetBranchTextBox = CreateReadOnlyTextBox(
                UiLocalization.Text("GitBranchIntegrateForm.Accessible.TargetBranch", "Selected branch"),
                targetBranch);
            _comparisonTextBox = CreateReadOnlyTextBox(
                UiLocalization.Text("GitBranchIntegrateForm.Accessible.Comparison", "Comparison"),
                BuildComparisonText(comparison));
            _comparisonTextBox.Multiline = true;
            TextBoxLineEndingNormalizer.NormalizeTextBox(_comparisonTextBox);
            _comparisonTextBox.Height = 88;
            _comparisonTextBox.Dock = DockStyle.Fill;

            _modeComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("GitBranchIntegrateForm.Accessible.Mode", "Operation"),
                TabIndex = 3
            };
            UiTheme.ApplyComboBox(_modeComboBox);
            _modeComboBox.Items.Add(new ModeOption(
                UiLocalization.Text("GitBranchIntegrateForm.Mode.Merge", "Merge selected branch into current branch"),
                GitBranchIntegrationMode.Merge));
            _modeComboBox.Items.Add(new ModeOption(
                UiLocalization.Text("GitBranchIntegrateForm.Mode.SquashMerge", "Squash merge selected branch into current branch"),
                GitBranchIntegrationMode.SquashMerge));
            _modeComboBox.Items.Add(new ModeOption(
                UiLocalization.Text("GitBranchIntegrateForm.Mode.Rebase", "Rebase current branch onto selected branch"),
                GitBranchIntegrationMode.Rebase));
            _modeComboBox.SelectedIndex = 0;

            _fetchFirstCheckBox = new CheckBox
            {
                AutoSize = true,
                Text = UiLocalization.Text("GitBranchIntegrateForm.Check.FetchFirst", "Fetch remote updates first(&F)"),
                AccessibleName = UiLocalization.Text("GitBranchIntegrateForm.Accessible.FetchFirst", "Fetch first"),
                TabIndex = 4
            };
            UiTheme.ApplyCheckBox(_fetchFirstCheckBox);

            _startButton = CreateButton(
                UiLocalization.Text("GitBranchIntegrateForm.Button.Start", "Start(&S)"),
                UiLocalization.Text("GitBranchIntegrateForm.Accessible.Start", "Start"),
                5);
            _cancelButton = CreateButton(
                UiLocalization.Text("GitBranchIntegrateForm.Button.Cancel", "Cancel(&C)"),
                UiLocalization.Text("GitBranchIntegrateForm.Accessible.Cancel", "Cancel"),
                6);
            UiTheme.ApplyButton(_startButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _startButton.Click += StartButton_Click;
            _cancelButton.Click += (sender, e) => DialogResult = DialogResult.Cancel;
            Shown += (sender, e) =>
            {
                Activate();
                if (_modeComboBox.CanFocus)
                {
                    _modeComboBox.Focus();
                }
            };

            BuildLayout();
            AcceptButton = _startButton;
            CancelButton = _cancelButton;
        }

        public GitBranchIntegrationMode SelectedMode { get; private set; }

        public bool FetchFirst
        {
            get { return _fetchFirstCheckBox.Checked; }
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            var option = _modeComboBox.SelectedItem as ModeOption;
            if (option == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitBranchIntegrateForm.Message.SelectMode", "Select an operation."),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            SelectedMode = option.Mode;
            DialogResult = DialogResult.OK;
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(CreateLabeledReadOnlyBox(
                UiLocalization.Text("GitBranchIntegrateForm.Label.CurrentBranch", "Current branch(&U)"),
                _currentBranchTextBox), 0, 0);
            root.Controls.Add(CreateLabeledReadOnlyBox(
                UiLocalization.Text("GitBranchIntegrateForm.Label.TargetBranch", "Selected branch(&T)"),
                _targetBranchTextBox), 0, 1);
            root.Controls.Add(CreateLabeledReadOnlyBox(
                UiLocalization.Text("GitBranchIntegrateForm.Label.Comparison", "Comparison(&M)"),
                _comparisonTextBox), 0, 2);
            root.Controls.Add(CreateLabeledComboBox(
                UiLocalization.Text("GitBranchIntegrateForm.Label.Mode", "Operation(&O)"),
                _modeComboBox), 0, 3);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, UiTheme.ControlSpacing, 0, 0),
                TabStop = false
            };
            footer.Controls.Add(_fetchFirstCheckBox);
            footer.Controls.Add(_startButton);
            footer.Controls.Add(_cancelButton);
            root.Controls.Add(footer, 0, 4);

            Controls.Add(root);
        }

        private static Control CreateLabeledReadOnlyBox(string labelText, Control control)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, UiTheme.ControlSpacing),
                TabStop = false
            };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panel.Controls.Add(CreateLabel(labelText), 0, 0);
            panel.Controls.Add(control, 0, 1);
            return panel;
        }

        private static Control CreateLabeledComboBox(string labelText, ComboBox comboBox)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, UiTheme.ControlSpacing),
                TabStop = false
            };
            panel.Controls.Add(CreateLabel(labelText), 0, 0);
            panel.Controls.Add(comboBox, 0, 1);
            return panel;
        }

        private static Label CreateLabel(string text)
        {
            var label = new Label
            {
                AutoSize = true,
                Text = text,
                Margin = new Padding(0, 0, 0, 4),
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private static TextBox CreateReadOnlyTextBox(string accessibleName, string text)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Top,
                ReadOnly = true,
                AccessibleName = accessibleName,
                Text = text ?? string.Empty,
                TabStop = false
            };
            UiTheme.ApplyTextBox(textBox, false, true);
            return textBox;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                AutoSize = true,
                Text = text,
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
        }

        private static string BuildComparisonText(GitBranchComparison comparison)
        {
            if (comparison == null)
            {
                return UiLocalization.Text("GitBranchIntegrateForm.Value.ComparisonUnknown", "Branch comparison is unavailable.");
            }

            return string.Format(
                UiLocalization.Text(
                    "GitBranchIntegrateForm.Value.Comparison.Template",
                    "Current-only commits: {0}\r\nSelected-only commits: {1}"),
                comparison.CurrentOnlyCount,
                comparison.TargetOnlyCount);
        }

        private sealed class ModeOption
        {
            public ModeOption(string text, GitBranchIntegrationMode mode)
            {
                Text = text ?? string.Empty;
                Mode = mode;
            }

            public string Text { get; private set; }

            public GitBranchIntegrationMode Mode { get; private set; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
