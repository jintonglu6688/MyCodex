using System.Drawing;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class OptionsForm
    {
        private CheckBox _mainWindowHotKeyEnabledCheckBox;
        private Label _mainWindowHotKeyStateLabel;
        private CheckBox _automaticChatReadingCheckBox;
        private Label _targetOptionsLabel;

        private void InitializeTargetPreferenceControls(AppConfigSnapshot config)
        {
            _targetOptionsLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                TabStop = false,
                Visible = false
            };
            _mainWindowHotKeyEnabledCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("OptionsForm.Check.MainWindowHotKeyEnabled", "Enable this target's show/hide shortcut"),
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.MainWindowHotKeyEnabled", "Enable target shortcut"),
                Checked = config.MainWindowHotKeyEnabled != false,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TabIndex = 0
            };
            UiTheme.ApplyCheckBox(_mainWindowHotKeyEnabledCheckBox);
            _mainWindowHotKeyTextBox.TabIndex = 1;
            _mainWindowHotKeyTextBox.Enabled = _mainWindowHotKeyEnabledCheckBox.Checked;
            _mainWindowHotKeyEnabledCheckBox.CheckedChanged += delegate
            {
                _mainWindowHotKeyTextBox.Enabled = _mainWindowHotKeyEnabledCheckBox.Checked;
            };
            _mainWindowHotKeyStateLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                TabStop = false
            };
            _automaticChatReadingCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("OptionsForm.Check.AutomaticChatReading", "Automatically read new replies"),
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.AutomaticChatReading", "Automatically read new replies"),
                Checked = config.EnableAutomaticChatReading,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TabIndex = 0
            };
            UiTheme.ApplyCheckBox(_automaticChatReadingCheckBox);
        }

        internal void SetMainWindowHotKeyStateText(string text)
        {
            _mainWindowHotKeyStateLabel.Text = text ?? string.Empty;
        }

        internal void SetTargetDisplayName(string name)
        {
            _targetOptionsLabel.Text = string.Format(UiLocalization.Text(
                "OptionsForm.TargetScope.Template", "These options apply only to {0}."), name);
            _targetOptionsLabel.Visible = true;
        }

        private Control CreateTargetHotKeyPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 3,
                TabIndex = 5,
                TabStop = false,
                Margin = Padding.Empty
            };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(_mainWindowHotKeyEnabledCheckBox, 0, 0);
            panel.Controls.Add(_mainWindowHotKeyTextBox, 0, 1);
            panel.Controls.Add(_mainWindowHotKeyStateLabel, 0, 2);
            return panel;
        }

        private Control CreateTargetScreenReaderPage()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                TabStop = false,
                Padding = new Padding(UiTheme.OuterPadding)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panel.Controls.Add(_automaticChatReadingCheckBox, 0, 0);
            panel.Controls.Add(new Label
            {
                Text = UiLocalization.Text("OptionsForm.AutomaticChatReading.BackgroundHint", "When enabled, new replies are also read while this target is in the background or hidden in the tray."),
                AutoSize = true,
                Dock = DockStyle.Fill,
                MaximumSize = new Size(650, 0),
                TabStop = false
            }, 0, 1);
            var settings = PrepareEmbeddedForm(_screenReaderSettingsForm);
            settings.TabIndex = 1;
            panel.Controls.Add(settings, 0, 2);
            return panel;
        }
    }
}
