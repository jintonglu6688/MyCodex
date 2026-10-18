using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Skills;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class InsertSkillForm : Form
    {
        private readonly ListView _skillListView;
        private readonly Label _statusLabel;
        private readonly Button _insertButton;
        private readonly Button _closeButton;
        private readonly string _workspacePath;
        private readonly IReadOnlyCollection<AppServerPluginSummary> _pluginSources;

        public InsertSkillForm(IReadOnlyCollection<AppServerSkillInfo> skills)
            : this(skills, null, null)
        {
        }

        public InsertSkillForm(
            IReadOnlyCollection<AppServerSkillInfo> skills,
            string workspacePath,
            IReadOnlyCollection<AppServerPluginSummary> pluginSources)
        {
            _workspacePath = workspacePath;
            _pluginSources = pluginSources ?? new List<AppServerPluginSummary>();
            Text = UiLocalization.Text("InsertSkillForm.Title", "Insert Skill");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(760, 420);
            UiTheme.ApplyForm(this);

            _skillListView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details,
                AccessibleName = UiLocalization.Text("InsertSkillForm.Accessible.Skills", "Skills"),
                TabIndex = 0
            };
            UiTheme.ApplyListView(_skillListView);
            _skillListView.Columns.Add(UiLocalization.Text("InsertSkillForm.Column.Skill", "Skill"), 300);
            _skillListView.Columns.Add(UiLocalization.Text("InsertSkillForm.Column.Enabled", "Enabled"), 90);
            _skillListView.Columns.Add(UiLocalization.Text("InsertSkillForm.Column.Source", "Source"), 260);
            _skillListView.ItemActivate += SkillListView_ItemActivate;
            _skillListView.SelectedIndexChanged += SkillListView_SelectedIndexChanged;

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };

            _insertButton = new Button
            {
                Text = UiLocalization.Text("InsertSkillForm.Button.Insert", "Insert(&I)"),
                AccessibleName = UiLocalization.Text("InsertSkillForm.Accessible.Insert", "Insert"),
                Width = 96,
                Height = 32,
                UseMnemonic = true,
                TabIndex = 1
            };
            UiTheme.ApplyButton(_insertButton, UiGlyph.Ok);
            _insertButton.Click += InsertButton_Click;

            _closeButton = new Button
            {
                Text = UiLocalization.Text("InsertSkillForm.Button.Close", "Close(&C)"),
                AccessibleName = UiLocalization.Text("InsertSkillForm.Accessible.Close", "Close"),
                Width = 96,
                Height = 32,
                UseMnemonic = true,
                TabIndex = 2
            };
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            _closeButton.Click += CloseButton_Click;

            BuildLayout();
            BindSkills(skills);

            AcceptButton = _insertButton;
            CancelButton = _closeButton;
            Shown += InsertSkillForm_Shown;
        }

        public AppServerSkillInfo SelectedSkill { get; private set; }

        private void BuildLayout()
        {
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            buttonPanel.Controls.Add(_closeButton);
            buttonPanel.Controls.Add(_insertButton);

            var footerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footerLayout.Controls.Add(_statusLabel, 0, 0);
            footerLayout.Controls.Add(buttonPanel, 1, 0);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_skillListView, 0, 0);
            root.Controls.Add(footerLayout, 0, 1);

            Controls.Add(root);
        }

        private void BindSkills(IReadOnlyCollection<AppServerSkillInfo> skills)
        {
            _skillListView.BeginUpdate();
            try
            {
                _skillListView.Items.Clear();
                foreach (var skill in (skills ?? Array.Empty<AppServerSkillInfo>())
                    .Where(item => item != null)
                    .OrderByDescending(item => item.Enabled)
                    .ThenBy(item => BuildDisplayName(item), StringComparer.CurrentCultureIgnoreCase))
                {
                    var item = new ListViewItem(BuildDisplayName(skill));
                    item.SubItems.Add(skill.Enabled
                        ? UiLocalization.Text("InsertSkillForm.Value.Yes", "Yes")
                        : UiLocalization.Text("InsertSkillForm.Value.No", "No"));
                    item.SubItems.Add(SkillSourceResolver.Resolve(skill, _workspacePath, _pluginSources).DisplayText);
                    item.Tag = skill;
                    _skillListView.Items.Add(item);
                }
            }
            finally
            {
                _skillListView.EndUpdate();
            }

            _statusLabel.Text = string.Format(
                UiLocalization.Text("InsertSkillForm.Status.TotalSkills", "Total {0} skills"),
                _skillListView.Items.Count);
            _insertButton.Enabled = _skillListView.Items.Count > 0;
        }

        private void InsertSkillForm_Shown(object sender, EventArgs e)
        {
            if (_skillListView.Items.Count > 0)
            {
                _skillListView.Items[0].Selected = true;
                _skillListView.Items[0].Focused = true;
                _skillListView.EnsureVisible(0);
                _skillListView.Focus();
            }
            else
            {
                _closeButton.Focus();
            }
        }

        private void SkillListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            _insertButton.Enabled = GetSelectedSkill() != null;
        }

        private void SkillListView_ItemActivate(object sender, EventArgs e)
        {
            ConfirmSelection();
        }

        private void InsertButton_Click(object sender, EventArgs e)
        {
            ConfirmSelection();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void ConfirmSelection()
        {
            var skill = GetSelectedSkill();
            if (skill == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("InsertSkillForm.Message.SelectSkill", "Please select a skill to insert."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _skillListView.Focus();
                return;
            }

            SelectedSkill = skill;
            DialogResult = DialogResult.OK;
            Close();
        }

        private AppServerSkillInfo GetSelectedSkill()
        {
            if (_skillListView.SelectedItems.Count == 0)
            {
                return null;
            }

            return _skillListView.SelectedItems[0].Tag as AppServerSkillInfo;
        }

        private static string BuildDisplayName(AppServerSkillInfo skill)
        {
            if (skill == null)
            {
                return UiLocalization.Text("InsertSkillForm.Value.UnnamedSkill", "Unnamed Skill");
            }

            if (!string.IsNullOrWhiteSpace(skill.DisplayName))
            {
                return skill.DisplayName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(skill.Name))
            {
                return skill.Name.Trim();
            }

            return UiLocalization.Text("InsertSkillForm.Value.UnnamedSkill", "Unnamed Skill");
        }
    }
}
