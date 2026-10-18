using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitUpstreamEditForm : Form
    {
        private readonly TextBox _branchTextBox;
        private readonly ComboBox _upstreamComboBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public GitUpstreamEditForm(string branchName, string upstream, IReadOnlyList<GitBranchEntry> remoteBranches)
        {
            Text = UiLocalization.Text("GitUpstreamEditForm.Title", "Set Upstream Branch");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(640, 170);
            UiTheme.ApplyForm(this);

            _branchTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitUpstreamEditForm.Accessible.Branch", "Local branch"), 0);
            _branchTextBox.ReadOnly = true;
            _branchTextBox.Text = branchName ?? string.Empty;
            _upstreamComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("GitUpstreamEditForm.Accessible.Upstream", "Upstream branch"), 1, DockStyle.Fill);
            PopulateUpstreamComboBox(remoteBranches, upstream);
            _okButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitUpstreamEditForm.Button.Save", "Save(&S)"),
                UiLocalization.Text("GitUpstreamEditForm.Accessible.Save", "Save"),
                2,
                112);
            _cancelButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitUpstreamEditForm.Button.Cancel", "&Cancel"),
                UiLocalization.Text("GitUpstreamEditForm.Accessible.Cancel", "Cancel"),
                3,
                112);
            UiTheme.ApplyButton(_okButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _okButton.Click += (sender, e) => AcceptValue();
            _cancelButton.Click += (sender, e) => DialogResult = DialogResult.Cancel;
            BuildLayout();
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            Shown += (sender, e) =>
            {
                _upstreamComboBox.Focus();
            };
        }

        public string Upstream
        {
            get { return (_upstreamComboBox.Text ?? string.Empty).Trim(); }
        }

        private void AcceptValue()
        {
            if (string.IsNullOrWhiteSpace(Upstream))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitUpstreamEditForm.Message.UpstreamRequired", "Enter the upstream first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _upstreamComboBox.Focus();
                return;
            }

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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.Controls.Add(UiControlFactory.CreateMutedLabel(UiLocalization.Text("GitUpstreamEditForm.Label.Branch", "Local branch(&B)")), 0, 0);
            root.Controls.Add(_branchTextBox, 0, 1);
            root.Controls.Add(UiControlFactory.CreateMutedLabel(UiLocalization.Text("GitUpstreamEditForm.Label.Upstream", "Upstream branch(&U)")), 0, 2);
            root.Controls.Add(_upstreamComboBox, 0, 3);

            var footer = UiControlFactory.CreateDialogButtonBar(UiTheme.WindowBackColor);
            footer.Controls.Add(_cancelButton);
            footer.Controls.Add(_okButton);
            root.Controls.Add(footer, 0, 4);
            Controls.Add(root);
        }

        private void PopulateUpstreamComboBox(IReadOnlyList<GitBranchEntry> remoteBranches, string upstream)
        {
            var selected = (upstream ?? string.Empty).Trim();
            var values = (remoteBranches ?? new GitBranchEntry[0])
                .Where(branch => branch != null && branch.IsRemote && !string.IsNullOrWhiteSpace(branch.Name))
                .Select(branch => branch.Name.Trim())
                .Distinct()
                .OrderBy(name => name, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var value in values)
            {
                _upstreamComboBox.Items.Add(value);
            }

            if (!string.IsNullOrWhiteSpace(selected) && !_upstreamComboBox.Items.Contains(selected))
            {
                _upstreamComboBox.Items.Add(selected);
            }

            if (!string.IsNullOrWhiteSpace(selected))
            {
                _upstreamComboBox.SelectedItem = selected;
            }
            else if (_upstreamComboBox.Items.Count > 0)
            {
                _upstreamComboBox.SelectedIndex = 0;
            }
        }
    }
}
