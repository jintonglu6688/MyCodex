using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public class ModelSettingsForm : Form
    {
        private readonly ComboBox _modelComboBox;
        private readonly ComboBox _effortComboBox;
        private readonly TextBox _descriptionTextBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly string _defaultOptionText;
        private readonly string _defaultDescriptionText;
        private readonly bool _allowDefaultOption;
        private readonly bool _showReasoningEffort;
        private readonly Func<string, bool> _supportsReasoningEffortForModel;
        private string _unlistedModelId;

        public ModelSettingsForm(
            string title,
            IReadOnlyList<AppServerModelOption> models,
            string selectedModelId,
            string selectedReasoningEffort,
            string defaultOptionText,
            string defaultDescriptionText,
            bool allowDefaultOption = true,
            bool showReasoningEffort = true,
            Func<string, bool> supportsReasoningEffortForModel = null)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 360);
            Size = new Size(640, 420);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _defaultOptionText = string.IsNullOrWhiteSpace(defaultOptionText)
                ? UiLocalization.Text("Common.Default", "Default")
                : defaultOptionText;
            _defaultDescriptionText = string.IsNullOrWhiteSpace(defaultDescriptionText)
                ? UiLocalization.Text("ModelSettingsForm.Description.Default", "Use default model settings.")
                : defaultDescriptionText;
            _allowDefaultOption = allowDefaultOption;
            _showReasoningEffort = showReasoningEffort;
            _supportsReasoningEffortForModel = supportsReasoningEffortForModel;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var modelLabel = new Label
            {
                Text = UiLocalization.Text("ModelSettingsForm.Label.ModelVersion", "Model Version"),
                AccessibleName = UiLocalization.Text("ModelSettingsForm.Accessible.ModelVersion", "Model Version"),
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TabStop = false
            };
            UiTheme.ApplyLabel(modelLabel, true);
            var effortLabel = new Label
            {
                Text = UiLocalization.Text("ModelSettingsForm.Label.ReasoningEffort", "Reasoning Effort"),
                AccessibleName = UiLocalization.Text("ModelSettingsForm.Accessible.ReasoningEffort", "Reasoning Effort"),
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TabStop = false
            };
            UiTheme.ApplyLabel(effortLabel, true);
            var descriptionLabel = new Label
            {
                Text = UiLocalization.Text("ModelSettingsForm.Label.Description", "Description"),
                AccessibleName = UiLocalization.Text("ModelSettingsForm.Accessible.Description", "Description"),
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TabStop = false
            };
            UiTheme.ApplyLabel(descriptionLabel, true);

            _modelComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("ModelSettingsForm.Accessible.ModelVersion", "Model Version"),
                TabIndex = 0
            };
            UiTheme.ApplyComboBox(_modelComboBox);
            _modelComboBox.SelectedIndexChanged += ModelComboBox_SelectedIndexChanged;

            _effortComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("ModelSettingsForm.Accessible.ReasoningEffort", "Reasoning Effort"),
                TabIndex = showReasoningEffort ? 1 : 99,
                TabStop = showReasoningEffort,
                Visible = showReasoningEffort
            };
            UiTheme.ApplyComboBox(_effortComboBox);
            _effortComboBox.SelectedIndexChanged += EffortComboBox_SelectedIndexChanged;

            _descriptionTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = UiLocalization.Text("ModelSettingsForm.Accessible.Description", "Description"),
                TabIndex = showReasoningEffort ? 2 : 1
            };
            UiTheme.ApplyTextBox(_descriptionTextBox, false, true);

            var buttonPanel = UiControlFactory.CreateDialogButtonBar();

            _okButton = new Button
            {
                Text = UiLocalization.Text("Common.OK", "&OK"),
                AccessibleName = UiLocalization.Text("Common.Accessible.OK", "OK"),
                AutoSize = true,
                TabIndex = showReasoningEffort ? 3 : 2
            };
            UiTheme.ApplyDialogButton(_okButton, UiGlyph.Ok);
            _okButton.Click += OkButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("Common.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = showReasoningEffort ? 4 : 3,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_okButton);

            root.Controls.Add(modelLabel, 0, 0);
            root.Controls.Add(_modelComboBox, 1, 0);
            root.Controls.Add(effortLabel, 0, 1);
            root.Controls.Add(_effortComboBox, 1, 1);
            root.Controls.Add(descriptionLabel, 0, 2);
            root.Controls.Add(_descriptionTextBox, 1, 2);
            root.Controls.Add(buttonPanel, 0, 3);
            root.SetColumnSpan(buttonPanel, 2);

            if (!showReasoningEffort)
            {
                effortLabel.Visible = false;
                root.RowStyles[1] = new RowStyle(SizeType.Absolute, 0F);
            }

            Controls.Add(root);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            PopulateModelChoices(models, selectedModelId, selectedReasoningEffort);
        }

        public string SelectedModelId { get; private set; }

        public string SelectedReasoningEffort { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _modelComboBox.Focus();
        }

        private void PopulateModelChoices(IReadOnlyList<AppServerModelOption> models, string selectedModelId, string selectedReasoningEffort)
        {
            _modelComboBox.Items.Clear();

            ModelChoiceItem defaultItem = null;
            if (_allowDefaultOption)
            {
                defaultItem = new ModelChoiceItem(null, _defaultOptionText, _defaultDescriptionText, null, null);
                _modelComboBox.Items.Add(defaultItem);
            }

            var hasSelectedModel = false;
            if (models != null)
            {
                foreach (var model in models)
                {
                    if (model == null || string.IsNullOrWhiteSpace(model.ModelId))
                    {
                        continue;
                    }

                    var item = new ModelChoiceItem(
                        model.ModelId,
                        MainFormModelOptionComposer.BuildModelMenuLabel(model),
                        BuildModelDescription(model),
                        model.DefaultReasoningEffort,
                        BuildEffortChoices(model));
                    _modelComboBox.Items.Add(item);

                    if (string.Equals(model.ModelId, selectedModelId, StringComparison.OrdinalIgnoreCase))
                    {
                        hasSelectedModel = true;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(selectedModelId) && !hasSelectedModel)
            {
                _unlistedModelId = selectedModelId;
                _modelComboBox.Items.Add(new ModelChoiceItem(
                    selectedModelId,
                    BuildModelLabel(selectedModelId),
                    UiLocalization.Text("ModelSettingsForm.Message.ModelNotInList", "The currently configured model is not in the available list."),
                    selectedReasoningEffort,
                    Array.Empty<EffortChoiceItem>()));
            }

            for (var i = 0; i < _modelComboBox.Items.Count; i++)
            {
                var item = _modelComboBox.Items[i] as ModelChoiceItem;
                if (item != null && string.Equals(item.ModelId, selectedModelId, StringComparison.OrdinalIgnoreCase))
                {
                    _modelComboBox.SelectedIndex = i;
                    UpdateEffortChoices(item, selectedReasoningEffort);
                    return;
                }
            }

            _modelComboBox.SelectedIndex = _modelComboBox.Items.Count > 0 ? 0 : -1;
            UpdateEffortChoices(_modelComboBox.SelectedItem as ModelChoiceItem, null);
        }

        private void ModelComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateEffortChoices(_modelComboBox.SelectedItem as ModelChoiceItem, null);
        }

        private void EffortComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateDescription();
        }

        private void UpdateEffortChoices(ModelChoiceItem modelChoice, string preferredEffortId)
        {
            _effortComboBox.Items.Clear();

            if (!_showReasoningEffort)
            {
                _effortComboBox.Enabled = false;
                UpdateDescription();
                return;
            }

            if (modelChoice == null || string.IsNullOrWhiteSpace(modelChoice.ModelId))
            {
                _effortComboBox.Enabled = false;
                UpdateDescription();
                return;
            }

            if (!SupportsReasoningEffortForModel(modelChoice.ModelId))
            {
                _effortComboBox.Enabled = false;
                UpdateDescription();
                return;
            }

            _effortComboBox.Enabled = true;
            if (_allowDefaultOption)
            {
                _effortComboBox.Items.Add(new EffortChoiceItem(null,
                    UiLocalization.Text("Common.Default", "Default"), string.Empty));
            }
            foreach (var effort in modelChoice.Efforts)
            {
                _effortComboBox.Items.Add(effort);
            }

            if (string.Equals(modelChoice.ModelId, _unlistedModelId, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(preferredEffortId) &&
                !_effortComboBox.Items.OfType<EffortChoiceItem>().Any(item =>
                    string.Equals(item.EffortId, preferredEffortId, StringComparison.OrdinalIgnoreCase)))
            {
                _effortComboBox.Items.Add(new EffortChoiceItem(preferredEffortId,
                    GetEffortText(preferredEffortId), string.Empty));
            }

            for (var i = 0; i < _effortComboBox.Items.Count; i++)
            {
                var item = _effortComboBox.Items[i] as EffortChoiceItem;
                if (item != null && string.Equals(item.EffortId, preferredEffortId, StringComparison.OrdinalIgnoreCase))
                {
                    _effortComboBox.SelectedIndex = i;
                    UpdateDescription();
                    return;
                }
            }

            var defaultEffort = NormalizeEffortId(modelChoice.DefaultReasoningEffort);
            for (var i = 0; i < _effortComboBox.Items.Count; i++)
            {
                var item = _effortComboBox.Items[i] as EffortChoiceItem;
                if (item != null && string.Equals(item.EffortId, defaultEffort, StringComparison.OrdinalIgnoreCase))
                {
                    _effortComboBox.SelectedIndex = i;
                    UpdateDescription();
                    return;
                }
            }

            _effortComboBox.SelectedIndex = _effortComboBox.Items.Count > 0 ? 0 : -1;
            UpdateDescription();
        }

        private void UpdateDescription()
        {
            var modelChoice = _modelComboBox.SelectedItem as ModelChoiceItem;
            if (modelChoice == null || string.IsNullOrWhiteSpace(modelChoice.ModelId))
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_descriptionTextBox, _defaultDescriptionText);
                return;
            }

            var effortChoice = _effortComboBox.SelectedItem as EffortChoiceItem;
            var parts = new List<string>
            {
                modelChoice.Description
            };

            if (_showReasoningEffort &&
                SupportsReasoningEffortForModel(modelChoice.ModelId) &&
                effortChoice != null &&
                !string.IsNullOrWhiteSpace(effortChoice.Description))
            {
                parts.Add(UiLocalization.Text("ModelSettingsForm.Description.EffortPrefix", "Effort Description: ") + effortChoice.Description);
            }

            TextBoxCaretHelper.SetTextAndMoveToStart(_descriptionTextBox, string.Join(
                "\r\n\r\n",
                parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray()));
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            var modelChoice = _modelComboBox.SelectedItem as ModelChoiceItem;
            if (modelChoice == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("ModelSettingsForm.Message.SelectModel", "Please select a model."),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _modelComboBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(modelChoice.ModelId))
            {
                SelectedModelId = null;
                SelectedReasoningEffort = null;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            var effortChoice = _effortComboBox.SelectedItem as EffortChoiceItem;
            SelectedModelId = modelChoice.ModelId;
            SelectedReasoningEffort = !_showReasoningEffort || !SupportsReasoningEffortForModel(modelChoice.ModelId)
                ? null
                : effortChoice != null
                ? effortChoice.EffortId
                : null;
            DialogResult = DialogResult.OK;
            Close();
        }

        private bool SupportsReasoningEffortForModel(string modelId)
        {
            if (!string.IsNullOrWhiteSpace(modelId) &&
                string.Equals(modelId, _unlistedModelId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (_supportsReasoningEffortForModel != null)
            {
                return _supportsReasoningEffortForModel(modelId);
            }

            return _modelComboBox.Items
                .OfType<ModelChoiceItem>()
                .Any(item =>
                    string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase) &&
                    item.Efforts.Count > 0);
        }

        private static List<EffortChoiceItem> BuildEffortChoices(AppServerModelOption model)
        {
            var result = new List<EffortChoiceItem>();
            if (model != null)
            {
                foreach (var effort in model.ReasoningEfforts.OrderByDescending(item => GetEffortRank(item != null ? item.EffortId : null)))
                {
                    if (effort == null || string.IsNullOrWhiteSpace(effort.EffortId))
                    {
                        continue;
                    }

                    result.Add(new EffortChoiceItem(effort.EffortId, GetEffortText(effort.EffortId), effort.Description));
                }
            }

            return result;
        }

        private static string BuildModelDescription(AppServerModelOption model)
        {
            if (model == null)
            {
                return string.Empty;
            }

            var name = string.IsNullOrWhiteSpace(model.DisplayName) ? model.ModelId : model.DisplayName;
            return string.IsNullOrWhiteSpace(model.Description) ? name : name + " - " + model.Description;
        }

        private static string BuildModelLabel(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return UiLocalization.Text("Common.Default", "Default");
            }

            var marker = "gpt-";
            var index = modelId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var suffix = modelId.Substring(index + marker.Length);
                var endIndex = suffix.IndexOf('-');
                return endIndex >= 0
                    ? suffix.Substring(0, endIndex).Replace("-", " ") + " " + suffix.Substring(endIndex + 1).Replace("-", " ")
                    : suffix.Replace("-", " ");
            }

            return modelId.Replace("-", " ");
        }

        private static string GetEffortText(string effortId)
        {
            switch ((effortId ?? string.Empty).ToLowerInvariant())
            {
                case "minimal":
                    return UiLocalization.Text("ReasoningEffort.Minimal", "Minimal");
                case "low":
                    return UiLocalization.Text("ReasoningEffort.Low", "Low");
                case "medium":
                    return UiLocalization.Text("ReasoningEffort.Medium", "Medium");
                case "high":
                    return UiLocalization.Text("ReasoningEffort.High", "High");
                case "xhigh":
                    return UiLocalization.Text("ReasoningEffort.XHigh", "Extra High");
                case "max":
                    return UiLocalization.Text("ReasoningEffort.Max", "Max");
                case "ultra":
                    return UiLocalization.Text("ReasoningEffort.Ultra", "Ultra");
                case "none":
                    return UiLocalization.Text("ReasoningEffort.None", "None");
                default:
                    return string.IsNullOrWhiteSpace(effortId)
                        ? UiLocalization.Text("Common.Default", "Default")
                        : effortId;
            }
        }

        private static int GetEffortRank(string effortId)
        {
            switch ((effortId ?? string.Empty).ToLowerInvariant())
            {
                case "ultra":
                    return 7;
                case "max":
                    return 6;
                case "xhigh":
                    return 5;
                case "high":
                    return 4;
                case "medium":
                    return 3;
                case "low":
                    return 2;
                case "minimal":
                    return 1;
                case "none":
                    return 0;
                default:
                    return -1;
            }
        }

        private static string NormalizeEffortId(string effortId)
        {
            return string.IsNullOrWhiteSpace(effortId) ? null : effortId;
        }

        private sealed class ModelChoiceItem
        {
            public ModelChoiceItem(string modelId, string text, string description, string defaultReasoningEffort, IReadOnlyList<EffortChoiceItem> efforts)
            {
                ModelId = modelId;
                Text = text;
                Description = description;
                DefaultReasoningEffort = defaultReasoningEffort;
                Efforts = efforts ?? Array.Empty<EffortChoiceItem>();
            }

            public string ModelId { get; }

            public string Text { get; }

            public string Description { get; }

            public string DefaultReasoningEffort { get; }

            public IReadOnlyList<EffortChoiceItem> Efforts { get; }

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class EffortChoiceItem
        {
            public EffortChoiceItem(string effortId, string text, string description)
            {
                EffortId = NormalizeEffortId(effortId);
                Text = text;
                Description = description ?? string.Empty;
            }

            public string EffortId { get; }

            public string Text { get; }

            public string Description { get; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
