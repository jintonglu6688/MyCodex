using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Skills;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class SkillEditForm : Form
    {
        private readonly bool _isNew;
        private readonly SkillFileModel _loadedModel;
        private readonly IList<SkillSaveLocation> _saveLocations;
        private readonly Func<SkillFileModel, bool, SkillValidationResult> _validateModel;
        private readonly Func<SkillFileModel, bool, string> _buildTargetPath;
        private readonly Func<SkillFileModel, bool, Task<string>> _saveModelAsync;
        private readonly bool _canOpenFolder;
        private readonly ComboBox _locationComboBox;
        private readonly TextBox _nameTextBox;
        private readonly TextBox _displayNameTextBox;
        private readonly TextBox _shortDescriptionTextBox;
        private readonly TextBox _descriptionTextBox;
        private readonly TextBox _defaultPromptTextBox;
        private readonly TextBox _pathTextBox;
        private readonly TextBox _bodyTextBox;
        private readonly Button _saveButton;
        private readonly Button _validateButton;
        private readonly Button _openFolderButton;
        private readonly Button _cancelButton;

        internal SkillEditForm(
            string workspacePath,
            bool isNew,
            SkillFileModel loadedModel,
            IList<SkillSaveLocation> saveLocations,
            Func<SkillFileModel, bool, SkillValidationResult> validateModel,
            Func<SkillFileModel, bool, string> buildTargetPath,
            Func<SkillFileModel, bool, Task<string>> saveModelAsync,
            bool canOpenFolder)
        {
            _isNew = isNew;
            _loadedModel = loadedModel ?? throw new ArgumentNullException(nameof(loadedModel));
            _saveLocations = saveLocations ?? new List<SkillSaveLocation>();
            _validateModel = validateModel ?? throw new ArgumentNullException(nameof(validateModel));
            _buildTargetPath = buildTargetPath ?? throw new ArgumentNullException(nameof(buildTargetPath));
            _saveModelAsync = saveModelAsync ?? throw new ArgumentNullException(nameof(saveModelAsync));
            _canOpenFolder = canOpenFolder;

            Text = _isNew
                ? UiLocalization.Text("SkillEditForm.Title.New", "New Skill")
                : UiLocalization.Text("SkillEditForm.Title.Edit", "Edit Skill");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(860, 680);
            Size = new Size(980, 760);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _locationComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("SkillEditForm.Accessible.Location", "Save location"), 0, DockStyle.Top);
            _nameTextBox = CreateTextBox(UiLocalization.Text("SkillEditForm.Accessible.Name", "Internal name"), 1, false, false, !_isNew);
            _displayNameTextBox = CreateTextBox(UiLocalization.Text("SkillEditForm.Accessible.DisplayName", "Display name"), 2, false, false, false);
            _shortDescriptionTextBox = CreateTextBox(UiLocalization.Text("SkillEditForm.Accessible.ShortDescription", "Short description"), 3, false, false, false);
            _descriptionTextBox = CreateTextBox(UiLocalization.Text("SkillEditForm.Accessible.Description", "Trigger description"), 4, true, false, false);
            _defaultPromptTextBox = CreateTextBox(UiLocalization.Text("SkillEditForm.Accessible.DefaultPrompt", "Default prompt"), 5, true, true, false);
            _pathTextBox = CreateTextBox(UiLocalization.Text("SkillEditForm.Accessible.Path", "Save path"), 6, false, true, true);
            _bodyTextBox = CreateTextBox(UiLocalization.Text("SkillEditForm.Accessible.Body", "Skill body"), 7, true, true, false);

            _saveButton = new Button
            {
                Text = UiLocalization.Text("SkillEditForm.Button.Save", "Save(&S)"),
                AccessibleName = UiLocalization.Text("SkillEditForm.Accessible.Save", "Save"),
                AutoSize = true,
                TabIndex = 8,
                UseMnemonic = true
            };
            UiTheme.ApplyDialogButton(_saveButton, UiGlyph.Ok);

            _validateButton = new Button
            {
                Text = UiLocalization.Text("SkillEditForm.Button.Validate", "Validate(&V)"),
                AccessibleName = UiLocalization.Text("SkillEditForm.Accessible.Validate", "Validate"),
                AutoSize = true,
                TabIndex = 9,
                UseMnemonic = true
            };
            UiTheme.ApplyDialogButton(_validateButton, UiGlyph.Info);

            _openFolderButton = new Button
            {
                Text = UiLocalization.Text("SkillEditForm.Button.OpenFolder", "Open Folder(&O)"),
                AccessibleName = UiLocalization.Text("SkillEditForm.Accessible.OpenFolder", "Open folder"),
                AutoSize = true,
                TabIndex = 10,
                UseMnemonic = true
            };
            UiTheme.ApplyDialogButton(_openFolderButton, UiGlyph.Workspace);

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("SkillEditForm.Button.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("SkillEditForm.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = 11,
                UseMnemonic = true,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            PopulateLocations();
            LoadModel();
            BuildLayout();
            _openFolderButton.Enabled = _canOpenFolder;

            _locationComboBox.SelectedIndexChanged += LocationComboBox_SelectedIndexChanged;
            _nameTextBox.TextChanged += NameTextBox_TextChanged;
            _saveButton.Click += SaveButton_Click;
            _validateButton.Click += ValidateButton_Click;
            _openFolderButton.Click += OpenFolderButton_Click;

            AcceptButton = _saveButton;
            CancelButton = _cancelButton;
        }

        public string SavedSkillPath { get; private set; }

        public string SavedSkillDisplayName { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var focusControl = _isNew ? (Control)_nameTextBox : _displayNameTextBox;
            focusControl.Focus();
            var textBox = focusControl as TextBox;
            if (textBox != null)
            {
                textBox.SelectAll();
            }
        }

        private static TextBox CreateTextBox(string accessibleName, int tabIndex, bool multiline, bool isCode, bool isReadOnly)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = multiline,
                ReadOnly = isReadOnly,
                ScrollBars = multiline ? ScrollBars.Both : ScrollBars.None,
                WordWrap = !isCode,
                AcceptsReturn = multiline,
                AcceptsTab = false,
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, isCode, isReadOnly);
            return textBox;
        }

        private void PopulateLocations()
        {
            _locationComboBox.Items.Clear();
            var locations = _saveLocations;
            foreach (var location in locations)
            {
                _locationComboBox.Items.Add(location);
            }

            if (_loadedModel.Location != null)
            {
                var selectedIndex = FindLocationIndex(_loadedModel.Location);
                if (selectedIndex < 0)
                {
                    _locationComboBox.Items.Add(_loadedModel.Location);
                    selectedIndex = _locationComboBox.Items.Count - 1;
                }

                _locationComboBox.SelectedIndex = selectedIndex;
            }
            else if (_locationComboBox.Items.Count > 0)
            {
                _locationComboBox.SelectedIndex = 0;
            }

            _locationComboBox.Enabled = _isNew;
        }

        private int FindLocationIndex(SkillSaveLocation location)
        {
            if (location == null)
            {
                return -1;
            }

            for (var i = 0; i < _locationComboBox.Items.Count; i++)
            {
                var item = _locationComboBox.Items[i] as SkillSaveLocation;
                if (item != null &&
                    string.Equals(item.RootPath, location.RootPath, StringComparison.OrdinalIgnoreCase) &&
                    item.Kind == location.Kind)
                {
                    return i;
                }
            }

            return -1;
        }

        private void LoadModel()
        {
            _nameTextBox.Text = _loadedModel.Name ?? string.Empty;
            _displayNameTextBox.Text = _loadedModel.DisplayName ?? string.Empty;
            _shortDescriptionTextBox.Text = _loadedModel.ShortDescription ?? string.Empty;
            _descriptionTextBox.Text = _loadedModel.Description ?? string.Empty;
            _defaultPromptTextBox.Text = _loadedModel.DefaultPrompt ?? string.Empty;
            _bodyTextBox.Text = _loadedModel.Body ?? string.Empty;
            UpdatePathText();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 10,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            AddRow(root, 0, UiLocalization.Text("SkillEditForm.Label.Location", "Save Location"), UiLocalization.Text("SkillEditForm.Accessible.Location", "Save location"), _locationComboBox);
            AddRow(root, 1, UiLocalization.Text("SkillEditForm.Label.Name", "Internal Name"), UiLocalization.Text("SkillEditForm.Accessible.Name", "Internal name"), _nameTextBox);
            AddRow(root, 2, UiLocalization.Text("SkillEditForm.Label.DisplayName", "Display Name"), UiLocalization.Text("SkillEditForm.Accessible.DisplayName", "Display name"), _displayNameTextBox);
            AddRow(root, 3, UiLocalization.Text("SkillEditForm.Label.ShortDescription", "Short Description"), UiLocalization.Text("SkillEditForm.Accessible.ShortDescription", "Short description"), _shortDescriptionTextBox);
            AddRow(root, 4, UiLocalization.Text("SkillEditForm.Label.Description", "Trigger Description"), UiLocalization.Text("SkillEditForm.Accessible.Description", "Trigger description"), _descriptionTextBox);
            AddRow(root, 5, UiLocalization.Text("SkillEditForm.Label.DefaultPrompt", "Default Prompt"), UiLocalization.Text("SkillEditForm.Accessible.DefaultPrompt", "Default prompt"), _defaultPromptTextBox);
            AddRow(root, 6, UiLocalization.Text("SkillEditForm.Label.Path", "Save Path"), UiLocalization.Text("SkillEditForm.Accessible.Path", "Save path"), _pathTextBox);
            AddRow(root, 7, UiLocalization.Text("SkillEditForm.Label.Body", "Skill Body"), UiLocalization.Text("SkillEditForm.Accessible.Body", "Skill body"), _bodyTextBox);

            var hintTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Text = UiLocalization.Text(
                    "SkillEditForm.Hint",
                    "Internal name is the folder name and must use lowercase letters, numbers, and hyphens. Trigger description is written to SKILL.md front matter and decides when Codex should use this skill."),
                AccessibleName = UiLocalization.Text("SkillEditForm.Accessible.Hint", "Hint"),
                TabIndex = 8
            };
            UiTheme.ApplyTextBox(hintTextBox, false, true);
            TextBoxCaretHelper.MoveToStart(hintTextBox);
            root.Controls.Add(hintTextBox, 0, 8);
            root.SetColumnSpan(hintTextBox, 2);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                TabStop = false,
                TabIndex = 9
            };
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_openFolderButton);
            buttonPanel.Controls.Add(_validateButton);
            buttonPanel.Controls.Add(_saveButton);
            root.Controls.Add(buttonPanel, 0, 9);
            root.SetColumnSpan(buttonPanel, 2);

            Controls.Add(root);
        }

        private static void AddRow(TableLayoutPanel root, int row, string text, string accessibleName, Control control)
        {
            var label = CreateLabel(text, accessibleName, ContentAlignment.MiddleLeft);
            root.Controls.Add(label, 0, row);
            root.Controls.Add(control, 1, row);
        }

        private static Label CreateLabel(string text, string accessibleName, ContentAlignment alignment)
        {
            var label = new Label
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                TextAlign = alignment,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private void LocationComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdatePathText();
        }

        private void NameTextBox_TextChanged(object sender, EventArgs e)
        {
            if (_isNew)
            {
                UpdatePathText();
            }
        }

        private async void SaveButton_Click(object sender, EventArgs e)
        {
            SkillFileModel model;
            if (!TryBuildModel(out model))
            {
                return;
            }

            try
            {
                UseWaitCursor = true;
                _saveButton.Enabled = false;
                SavedSkillPath = await _saveModelAsync(model, _isNew);
                SavedSkillDisplayName = GetDisplayNameForStatus(model);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
                if (!IsDisposed)
                {
                    _saveButton.Enabled = true;
                }
            }
        }

        private void ValidateButton_Click(object sender, EventArgs e)
        {
            SkillFileModel model;
            if (!TryBuildModel(out model))
            {
                return;
            }

            MessageBox.Show(
                this,
                UiLocalization.Text("SkillEditForm.Message.ValidationPassed", "Validation passed."),
                AppConstants.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OpenFolderButton_Click(object sender, EventArgs e)
        {
            // The selected App Server owns this path. Direct shell access is intentionally unavailable.
        }

        private bool TryBuildModel(out SkillFileModel model)
        {
            model = new SkillFileModel
            {
                Name = _nameTextBox.Text,
                DisplayName = _displayNameTextBox.Text,
                ShortDescription = _shortDescriptionTextBox.Text,
                Description = _descriptionTextBox.Text,
                DefaultPrompt = _defaultPromptTextBox.Text,
                Body = _bodyTextBox.Text,
                SkillPath = _isNew ? null : _loadedModel.SkillPath,
                Location = GetSelectedLocation()
            };

            var validation = _validateModel(model, _isNew);
            if (validation.IsValid)
            {
                return true;
            }

            MessageBox.Show(this, validation.BuildMessage(), AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            FocusFirstInvalidControl(model);
            return false;
        }

        private void FocusFirstInvalidControl(SkillFileModel model)
        {
            if (model == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(model.Name))
            {
                _nameTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(model.Description))
            {
                _descriptionTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(model.Body))
            {
                _bodyTextBox.Focus();
            }
        }

        private SkillSaveLocation GetSelectedLocation()
        {
            return _locationComboBox.SelectedItem as SkillSaveLocation;
        }

        private void UpdatePathText()
        {
            var model = new SkillFileModel
            {
                Name = _nameTextBox.Text,
                SkillPath = _isNew ? null : _loadedModel.SkillPath,
                Location = GetSelectedLocation()
            };

            var path = _buildTargetPath(model, _isNew);
            if (string.IsNullOrWhiteSpace(path) && model.Location != null)
            {
                path = model.Location.RootPath;
            }

            _pathTextBox.Text = path ?? string.Empty;
        }

        private static string GetDisplayNameForStatus(SkillFileModel model)
        {
            if (model == null)
            {
                return UiLocalization.Text("SkillsManagementForm.Value.UnnamedSkill", "(Unnamed Skill)");
            }

            if (!string.IsNullOrWhiteSpace(model.DisplayName))
            {
                return model.DisplayName.Trim();
            }

            return !string.IsNullOrWhiteSpace(model.Name)
                ? model.Name.Trim()
                : UiLocalization.Text("SkillsManagementForm.Value.UnnamedSkill", "(Unnamed Skill)");
        }
    }
}
