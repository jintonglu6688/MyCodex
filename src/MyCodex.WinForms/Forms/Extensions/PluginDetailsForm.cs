using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class PluginDetailsForm : Form
    {
        private readonly IAppServerPluginClient _appServerClient;
        private readonly string _marketplaceName;
        private readonly string _remotePluginId;
        private readonly List<AppServerPluginSkillInfo> _skills;
        private readonly TextBox _detailTextBox;
        private readonly ComboBox _skillComboBox;
        private readonly Button _previewSkillButton;
        private readonly Label _statusLabel;
        private readonly Button _closeButton;

        public PluginDetailsForm(
            string title,
            string headerText,
            string detailText,
            IAppServerPluginClient appServerClient = null,
            string marketplaceName = null,
            string remotePluginId = null,
            IReadOnlyCollection<AppServerPluginSkillInfo> skills = null)
        {
            _appServerClient = appServerClient;
            _marketplaceName = string.IsNullOrWhiteSpace(marketplaceName) ? null : marketplaceName.Trim();
            _remotePluginId = string.IsNullOrWhiteSpace(remotePluginId) ? null : remotePluginId.Trim();
            _skills = skills == null
                ? new List<AppServerPluginSkillInfo>()
                : skills.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name)).ToList();

            Text = string.IsNullOrWhiteSpace(title)
                ? UiLocalization.Text("PluginDetailsForm.Title", "Plugin Details")
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
                    ? UiLocalization.Text("PluginDetailsForm.Header", "Plugin Details")
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
                AccessibleName = UiLocalization.Text("PluginDetailsForm.Accessible.Details", "Plugin Details"),
                TabIndex = 0,
                Text = detailText ?? string.Empty
            };
            UiTheme.ApplyTextBox(_detailTextBox, true, true);

            _skillComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 280,
                AccessibleName = UiLocalization.Text("PluginDetailsForm.Accessible.Skill", "Skill"),
                TabIndex = 1
            };
            UiTheme.ApplyComboBox(_skillComboBox);

            foreach (var skill in _skills)
            {
                _skillComboBox.Items.Add(new PluginSkillListItem(skill));
            }

            if (_skillComboBox.Items.Count > 0)
            {
                _skillComboBox.SelectedIndex = 0;
            }

            _previewSkillButton = new Button
            {
                Text = UiLocalization.Text("PluginDetailsForm.Button.PreviewSkill", "Preview Skill(&P)"),
                AutoSize = true,
                MinimumSize = new Size(132, 0),
                AccessibleName = UiLocalization.Text("PluginDetailsForm.Accessible.PreviewSkill", "Preview skill"),
                TabIndex = 2,
                Enabled = CanPreviewSkills()
            };
            UiTheme.ApplyButton(_previewSkillButton, UiGlyph.Search);
            _previewSkillButton.Click += PreviewSkillButton_Click;

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            _closeButton = new Button
            {
                Text = UiLocalization.Text("PluginDetailsForm.Button.Close", "&Close"),
                AutoSize = true,
                MinimumSize = new Size(108, 0),
                AccessibleName = UiLocalization.Text("PluginDetailsForm.Accessible.Close", "Close"),
                TabIndex = 3
            };
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            _closeButton.Click += CloseButton_Click;

            var previewPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 8, 0, 8),
                Padding = new Padding(0),
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                Visible = CanPreviewSkills()
            };
            previewPanel.Controls.Add(_skillComboBox);
            previewPanel.Controls.Add(_previewSkillButton);

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
                RowCount = 5,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(headerLabel, 0, 0);
            root.Controls.Add(_detailTextBox, 0, 1);
            root.Controls.Add(previewPanel, 0, 2);
            root.Controls.Add(_statusLabel, 0, 3);
            root.Controls.Add(actionPanel, 0, 4);

            Controls.Add(root);
            CancelButton = _closeButton;
            Shown += PluginDetailsForm_Shown;
        }

        private void PluginDetailsForm_Shown(object sender, EventArgs e)
        {
            _detailTextBox.Focus();
            TextBoxCaretHelper.MoveToStart(_detailTextBox);
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private async void PreviewSkillButton_Click(object sender, EventArgs e)
        {
            var selected = _skillComboBox.SelectedItem as PluginSkillListItem;
            if (selected == null || selected.Skill == null || !CanPreviewSkills())
            {
                return;
            }

            _previewSkillButton.Enabled = false;
            SetStatus(UiLocalization.Text("PluginDetailsForm.Status.ReadingSkill", "Reading skill description..."));
            try
            {
                var result = await _appServerClient.ReadPluginSkillAsync(
                    _marketplaceName,
                    _remotePluginId,
                    selected.Skill.Name,
                    CancellationToken.None);
                using (var form = new PluginSkillPreviewForm(
                    string.IsNullOrWhiteSpace(result.SkillName) ? selected.Text : result.SkillName,
                    result.Markdown))
                {
                    form.ShowDialog(this);
                }

                SetStatus(UiLocalization.Text("PluginDetailsForm.Status.SkillPreviewLoaded", "Skill description loaded."));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(
                    UiLocalization.Text("PluginDetailsForm.Status.SkillPreviewUnavailable.Template", "Skill description preview is unavailable: {0}"),
                    ex.Message));
            }
            finally
            {
                _previewSkillButton.Enabled = CanPreviewSkills();
            }
        }

        private bool CanPreviewSkills()
        {
            return _appServerClient != null &&
                   !string.IsNullOrWhiteSpace(_marketplaceName) &&
                   !string.IsNullOrWhiteSpace(_remotePluginId) &&
                   _skills.Count > 0;
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
            if (!string.IsNullOrWhiteSpace(_statusLabel.Text))
            {
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
        }

        private sealed class PluginSkillListItem
        {
            public PluginSkillListItem(AppServerPluginSkillInfo skill)
            {
                Skill = skill;
                Text = !string.IsNullOrWhiteSpace(skill.DisplayName)
                    ? skill.DisplayName.Trim()
                    : skill.Name.Trim();
            }

            public AppServerPluginSkillInfo Skill { get; private set; }

            public string Text { get; private set; }

            public override string ToString()
            {
                return Text;
            }
        }
    }

    internal sealed class PluginSkillPreviewForm : Form
    {
        private readonly TextBox _contentTextBox;
        private readonly Button _okButton;

        public PluginSkillPreviewForm(string skillName, string markdown)
        {
            Text = string.Format(
                UiLocalization.Text("PluginSkillPreviewForm.Title.Template", "Skill Description - {0}"),
                string.IsNullOrWhiteSpace(skillName)
                    ? UiLocalization.Text("PluginSkillPreviewForm.Value.Skill", "Skill")
                    : skillName.Trim());
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            Size = new Size(900, 680);
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);

            _contentTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = true,
                AccessibleName = UiLocalization.Text("PluginSkillPreviewForm.Accessible.Content", "Skill description"),
                TabIndex = 0,
                Text = markdown ?? string.Empty
            };
            UiTheme.ApplyTextBox(_contentTextBox, true, true);

            _okButton = new Button
            {
                Text = UiLocalization.Text("PluginSkillPreviewForm.Button.OK", "&OK"),
                AutoSize = true,
                MinimumSize = new Size(108, 0),
                AccessibleName = UiLocalization.Text("PluginSkillPreviewForm.Accessible.OK", "OK"),
                TabIndex = 1
            };
            UiTheme.ApplyDialogButton(_okButton, UiGlyph.Ok);
            _okButton.Click += OkButton_Click;

            var actionPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0, 8, 0, 0),
                Padding = new Padding(0),
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            actionPanel.Controls.Add(_okButton);

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
            root.Controls.Add(_contentTextBox, 0, 0);
            root.Controls.Add(actionPanel, 0, 1);

            Controls.Add(root);
            AcceptButton = _okButton;
            CancelButton = _okButton;
            Shown += PluginSkillPreviewForm_Shown;
        }

        private void PluginSkillPreviewForm_Shown(object sender, EventArgs e)
        {
            _contentTextBox.Focus();
            TextBoxCaretHelper.MoveToStart(_contentTextBox);
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
