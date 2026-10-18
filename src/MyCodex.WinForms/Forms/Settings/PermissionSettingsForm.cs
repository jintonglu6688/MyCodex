using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public class PermissionSettingsForm : Form
    {
        private readonly ComboBox _permissionComboBox;
        private readonly TextBox _descriptionTextBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly bool _allowInherit;
        private readonly string _inheritText;
        private readonly string _inheritDescription;
        private readonly bool _showKeepCurrentOption;
        private readonly string _keepCurrentText;
        private readonly string _keepCurrentDescription;

        public PermissionSettingsForm(
            string title,
            bool allowInherit,
            string inheritText,
            string inheritDescription,
            ApprovalMode? selectedMode,
            ApprovalMode effectiveMode,
            bool showKeepCurrentOption = false,
            string keepCurrentText = null,
            string keepCurrentDescription = null)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(520, 320);
            Size = new Size(600, 360);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _allowInherit = allowInherit;
            _inheritText = string.IsNullOrWhiteSpace(inheritText)
                ? UiLocalization.Text("PermissionSettingsForm.InheritText", "Follow Default")
                : inheritText;
            _inheritDescription = string.IsNullOrWhiteSpace(inheritDescription)
                ? string.Format(
                    UiLocalization.Text(
                        "PermissionSettingsForm.InheritDescription.Template",
                        "Use current default permission. Effective now: {0}."),
                    GetApprovalModeText(effectiveMode))
                : inheritDescription;
            _showKeepCurrentOption = showKeepCurrentOption;
            _keepCurrentText = string.IsNullOrWhiteSpace(keepCurrentText)
                ? UiLocalization.Text("PermissionSettingsForm.KeepCurrentText", "Keep Current Custom Setting")
                : keepCurrentText;
            _keepCurrentDescription = string.IsNullOrWhiteSpace(keepCurrentDescription)
                ? UiLocalization.Text(
                    "PermissionSettingsForm.KeepCurrentDescription",
                    "Current value is a custom setting. Choose Cancel if you do not want to override it.")
                : keepCurrentDescription;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var permissionLabel = new Label
            {
                Text = UiLocalization.Text("PermissionSettingsForm.Label.Permission", "Permission"),
                AccessibleName = UiLocalization.Text("PermissionSettingsForm.Accessible.Permission", "Permission"),
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TabStop = false
            };
            UiTheme.ApplyLabel(permissionLabel, true);
            var descriptionLabel = new Label
            {
                Text = UiLocalization.Text("PermissionSettingsForm.Label.Description", "Description"),
                AccessibleName = UiLocalization.Text("PermissionSettingsForm.Accessible.Description", "Description"),
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TabStop = false
            };
            UiTheme.ApplyLabel(descriptionLabel, true);

            _permissionComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("PermissionSettingsForm.Accessible.Permission", "Permission"),
                TabIndex = 0
            };
            UiTheme.ApplyComboBox(_permissionComboBox);
            _permissionComboBox.SelectedIndexChanged += PermissionComboBox_SelectedIndexChanged;

            _descriptionTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = UiLocalization.Text("PermissionSettingsForm.Accessible.Description", "Description"),
                TabIndex = 1
            };
            UiTheme.ApplyTextBox(_descriptionTextBox, false, true);

            var buttonPanel = UiControlFactory.CreateDialogButtonBar();

            _okButton = new Button
            {
                Text = UiLocalization.Text("PermissionSettingsForm.Button.OK", "&OK"),
                AccessibleName = UiLocalization.Text("PermissionSettingsForm.Accessible.OK", "OK"),
                AutoSize = true,
                TabIndex = 2
            };
            UiTheme.ApplyDialogButton(_okButton, UiGlyph.Ok);
            _okButton.Click += OkButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("PermissionSettingsForm.Button.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("PermissionSettingsForm.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = 3,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_okButton);

            root.Controls.Add(permissionLabel, 0, 0);
            root.Controls.Add(_permissionComboBox, 1, 0);
            root.Controls.Add(descriptionLabel, 0, 1);
            root.Controls.Add(_descriptionTextBox, 1, 1);
            root.Controls.Add(buttonPanel, 0, 2);
            root.SetColumnSpan(buttonPanel, 2);

            Controls.Add(root);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            PopulatePermissionChoices(selectedMode);
        }

        public ApprovalMode? SelectedApprovalMode { get; private set; }

        public bool KeepCurrentValue { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _permissionComboBox.Focus();
        }

        private void PopulatePermissionChoices(ApprovalMode? selectedMode)
        {
            if (selectedMode == ApprovalMode.FullAccessAutoApprove)
            {
                selectedMode = ApprovalMode.FullAccess;
            }
            _permissionComboBox.Items.Clear();

            if (_showKeepCurrentOption)
            {
                _permissionComboBox.Items.Add(new PermissionChoiceItem(
                    PermissionChoiceKind.KeepCurrent,
                    null,
                    _keepCurrentText,
                    _keepCurrentDescription));
            }

            if (_allowInherit)
            {
                _permissionComboBox.Items.Add(new PermissionChoiceItem(
                    PermissionChoiceKind.Inherit,
                    null,
                    _inheritText,
                    _inheritDescription));
            }

            _permissionComboBox.Items.Add(new PermissionChoiceItem(
                PermissionChoiceKind.Preset,
                ApprovalMode.ReadOnly,
                ApprovalModeDisplayText.NameWithDescription(ApprovalMode.ReadOnly),
                UiLocalization.Text(
                    "PermissionSettingsForm.OptionDescription.ReadOnly",
                    "Run in a read-only sandbox and request approval when needed.")));
            _permissionComboBox.Items.Add(new PermissionChoiceItem(
                PermissionChoiceKind.Preset,
                ApprovalMode.Auto,
                ApprovalModeDisplayText.NameWithDescription(ApprovalMode.Auto),
                UiLocalization.Text(
                    "PermissionSettingsForm.OptionDescription.Auto",
                    "Execute automatically in workspace sandbox and request approval for out-of-scope actions.")));
            _permissionComboBox.Items.Add(new PermissionChoiceItem(
                PermissionChoiceKind.Preset,
                ApprovalMode.FullAccess,
                ApprovalModeDisplayText.NameWithDescription(ApprovalMode.FullAccess),
                UiLocalization.Text(
                    "PermissionSettingsForm.OptionDescription.FullAccess",
                    "Access files outside the workspace and use the network. Supported command, file-change and permission requests are automatically approved, including destructive operations. No model review. Questions and sign-in still require your input; prohibited actions remain blocked.")));
            _permissionComboBox.Items.Add(new PermissionChoiceItem(
                PermissionChoiceKind.Preset,
                ApprovalMode.Custom,
                ApprovalModeDisplayText.NameWithDescription(ApprovalMode.Custom),
                UiLocalization.Text(
                    "PermissionSettingsForm.OptionDescription.Custom",
                    "Start from read-only access. Approval categories selected in Options are allowed automatically.")));

            for (var i = 0; i < _permissionComboBox.Items.Count; i++)
            {
                var item = _permissionComboBox.Items[i] as PermissionChoiceItem;
                if (item == null)
                {
                    continue;
                }

                if (item.Kind == PermissionChoiceKind.Preset && item.Mode == selectedMode)
                {
                    _permissionComboBox.SelectedIndex = i;
                    UpdateDescription();
                    return;
                }
            }

            if (_showKeepCurrentOption)
            {
                _permissionComboBox.SelectedIndex = 0;
                UpdateDescription();
                return;
            }

            _permissionComboBox.SelectedIndex = 0;
            UpdateDescription();
        }

        private void PermissionComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateDescription();
        }

        private void UpdateDescription()
        {
            var item = _permissionComboBox.SelectedItem as PermissionChoiceItem;
            TextBoxCaretHelper.SetTextAndMoveToStart(
                _descriptionTextBox,
                item != null ? item.Description : string.Empty);
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            var item = _permissionComboBox.SelectedItem as PermissionChoiceItem;
            if (item == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("PermissionSettingsForm.Message.SelectPermission", "Please select a permission mode."),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _permissionComboBox.Focus();
                return;
            }

            KeepCurrentValue = item.Kind == PermissionChoiceKind.KeepCurrent;
            SelectedApprovalMode = item.Mode;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string GetApprovalModeText(ApprovalMode approvalMode)
        {
            return ApprovalModeDisplayText.Name(approvalMode);
        }

        private enum PermissionChoiceKind
        {
            KeepCurrent,
            Inherit,
            Preset
        }

        private sealed class PermissionChoiceItem
        {
            public PermissionChoiceItem(PermissionChoiceKind kind, ApprovalMode? mode, string text, string description)
            {
                Kind = kind;
                Mode = mode;
                Text = text;
                Description = description ?? string.Empty;
            }

            public PermissionChoiceKind Kind { get; }

            public ApprovalMode? Mode { get; }

            public string Text { get; }

            public string Description { get; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
