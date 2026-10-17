using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public class ScreenReaderSettingsForm : Form
    {
        private readonly ComboBox _providerComboBox;
        private readonly CheckBox _manualBrowseMainChannelCheckBox;
        private readonly TextBox _independentChannelNameTextBox;
        private readonly TextBox _descriptionTextBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public ScreenReaderSettingsForm(
            ScreenReaderProviderKind selectedProvider,
            bool manualBrowseUseReaderMainChannel,
            string independentChannelName)
            : this(selectedProvider, manualBrowseUseReaderMainChannel, independentChannelName, true)
        {
        }

        internal ScreenReaderSettingsForm(
            ScreenReaderProviderKind selectedProvider,
            bool manualBrowseUseReaderMainChannel,
            string independentChannelName,
            bool showDialogButtons)
        {
            Text = UiLocalization.Text("ScreenReaderSettingsForm.Title", "Screen Reader Settings");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 360);
            Size = new Size(620, 420);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var providerLabel = new Label
            {
                Text = UiLocalization.Text("ScreenReaderSettingsForm.Label.Provider", "Screen Reader Interface"),
                AccessibleName = UiLocalization.Text("ScreenReaderSettingsForm.Accessible.Provider", "Screen Reader Interface"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(providerLabel, true);

            var optionLabel = new Label
            {
                Text = UiLocalization.Text("ScreenReaderSettingsForm.Label.ManualBrowse", "Manual Browse"),
                AccessibleName = UiLocalization.Text("ScreenReaderSettingsForm.Accessible.ManualBrowse", "Manual Browse"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(optionLabel, true);

            var channelNameLabel = new Label
            {
                Text = UiLocalization.Text("ScreenReaderSettingsForm.Label.IndependentChannelName", "Independent Channel Name"),
                AccessibleName = UiLocalization.Text("ScreenReaderSettingsForm.Accessible.IndependentChannelName", "Independent Channel Name"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(channelNameLabel, true);

            var descriptionLabel = new Label
            {
                Text = UiLocalization.Text("ScreenReaderSettingsForm.Label.Description", "Description"),
                AccessibleName = UiLocalization.Text("ScreenReaderSettingsForm.Accessible.Description", "Description"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(descriptionLabel, true);

            _providerComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("ScreenReaderSettingsForm.Accessible.Provider", "Screen Reader Interface"),
                TabIndex = 0
            };
            UiTheme.ApplyComboBox(_providerComboBox);
            _providerComboBox.Items.Add(new ProviderItem(ScreenReaderProviderKind.Auto, UiLocalization.Text("ScreenReaderSettingsForm.Provider.Auto", "Auto")));
            _providerComboBox.Items.Add(new ProviderItem(ScreenReaderProviderKind.ByCtrl, UiLocalization.Text("ScreenReaderSettingsForm.Provider.ByCtrl", "BaoYi YueTing")));
            _providerComboBox.Items.Add(new ProviderItem(ScreenReaderProviderKind.BaoYiWebApi, UiLocalization.Text("ScreenReaderSettingsForm.Provider.BaoYiWebApi", "保益悦听新接口")));
            _providerComboBox.Items.Add(new ProviderItem(ScreenReaderProviderKind.Zdsr, UiLocalization.Text("ScreenReaderSettingsForm.Provider.Zdsr", "ZDSR")));
            _providerComboBox.Items.Add(new ProviderItem(ScreenReaderProviderKind.Nvda, UiLocalization.Text("ScreenReaderSettingsForm.Provider.Nvda", "NVDA")));

            _manualBrowseMainChannelCheckBox = new CheckBox
            {
                AutoSize = true,
                Text = UiLocalization.Text("ScreenReaderSettingsForm.Option.ManualBrowseUseMainChannel", "Manual browse uses screen reader main channel (supported interfaces only)(&M)"),
                AccessibleName = UiLocalization.Text("ScreenReaderSettingsForm.Accessible.ManualBrowseUseMainChannel", "Manual browse uses screen reader main channel"),
                Margin = new Padding(0, 6, 0, 6),
                TabIndex = 1
            };
            UiTheme.ApplyCheckBox(_manualBrowseMainChannelCheckBox);

            _independentChannelNameTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                AccessibleName = UiLocalization.Text("ScreenReaderSettingsForm.Accessible.IndependentChannelName", "Independent Channel Name"),
                TabIndex = 2
            };
            UiTheme.ApplyTextBox(_independentChannelNameTextBox, false, false);

            _descriptionTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = UiLocalization.Text("ScreenReaderSettingsForm.Accessible.Description", "Description"),
                TabIndex = 3
            };
            UiTheme.ApplyTextBox(_descriptionTextBox, false, true);

            _okButton = new Button
            {
                Text = UiLocalization.Text("Common.OK", "&OK"),
                AccessibleName = UiLocalization.Text("Common.Accessible.OK", "OK"),
                AutoSize = true,
                TabIndex = 4
            };
            UiTheme.ApplyDialogButton(_okButton, UiGlyph.Ok);
            _okButton.Click += OkButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("Common.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = 5,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                TabStop = false
            };
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_okButton);
            buttonPanel.Visible = showDialogButtons;

            root.Controls.Add(providerLabel, 0, 0);
            root.Controls.Add(_providerComboBox, 1, 0);
            root.Controls.Add(optionLabel, 0, 1);
            root.Controls.Add(_manualBrowseMainChannelCheckBox, 1, 1);
            root.Controls.Add(channelNameLabel, 0, 2);
            root.Controls.Add(_independentChannelNameTextBox, 1, 2);
            root.Controls.Add(descriptionLabel, 0, 3);
            root.Controls.Add(_descriptionTextBox, 1, 3);
            root.Controls.Add(buttonPanel, 0, 4);
            root.SetColumnSpan(buttonPanel, 2);

            Controls.Add(root);
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            SelectProvider(selectedProvider);
            _manualBrowseMainChannelCheckBox.Checked = manualBrowseUseReaderMainChannel;
            _independentChannelNameTextBox.Text = independentChannelName ?? string.Empty;
            _providerComboBox.SelectedIndexChanged += ProviderComboBox_SelectedIndexChanged;
            UpdateDescription();
        }

        public ScreenReaderProviderKind SelectedProvider { get; private set; }

        public bool ManualBrowseUseReaderMainChannel { get; private set; }

        public string ScreenReaderIndependentChannelName { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _providerComboBox.Focus();
        }

        private void ProviderComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateDescription();
        }

        private void UpdateDescription()
        {
            var provider = GetSelectedProvider();
            var supportsMainChannelOption =
                provider == ScreenReaderProviderKind.Auto ||
                provider == ScreenReaderProviderKind.ByCtrl ||
                provider == ScreenReaderProviderKind.BaoYiWebApi ||
                provider == ScreenReaderProviderKind.Zdsr;
            var supportsIndependentChannelName =
                provider == ScreenReaderProviderKind.Auto ||
                provider == ScreenReaderProviderKind.ByCtrl ||
                provider == ScreenReaderProviderKind.BaoYiWebApi ||
                provider == ScreenReaderProviderKind.Zdsr;

            _manualBrowseMainChannelCheckBox.Enabled = supportsMainChannelOption;
            _independentChannelNameTextBox.Enabled = supportsIndependentChannelName;
            if (!_manualBrowseMainChannelCheckBox.Enabled)
            {
                _manualBrowseMainChannelCheckBox.Checked = false;
            }

            var builder = new StringBuilder();
            builder.AppendLine(
                string.Format(
                    UiLocalization.Text("ScreenReaderSettingsForm.Description.CurrentProvider", "Current interface: {0}"),
                    GetProviderText(provider)));
            builder.AppendLine();
            builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.ProviderGuideTitle", "Interface details:"));
            switch (provider)
            {
                case ScreenReaderProviderKind.ByCtrl:
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.ByCtrl.Line1", "BaoYi YueTing uses the new byctrl interface."));
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.ByCtrl.Line2", "Automatic reading uses the configured independent channel by default."));
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.ByCtrl.Line3", "Manual browse can use the main screen reader channel when the option above is checked."));
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.ByCtrl.Line4", "If independent channel name is provided, it is passed to the new interface directly."));
                    break;
                case ScreenReaderProviderKind.BaoYiWebApi:
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.BaoYiWebApi.Line1", "BaoYi YueTing Web API uses the local Web API service."));
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.BaoYiWebApi.Line2", "The service must be running at 127.0.0.1:26609."));
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.BaoYiWebApi.Line3", "This stage only supports manual selection and does not change the automatic order."));
                    break;
                case ScreenReaderProviderKind.Zdsr:
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.Zdsr.Line1", "ZDSR uses the updated API with main and independent channel support."));
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.Zdsr.Line2", "Automatic reading uses the configured independent channel by default."));
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.Zdsr.Line3", "Manual browse can use the main screen reader channel when the option above is checked."));
                    break;
                case ScreenReaderProviderKind.Nvda:
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.Nvda.Line1", "NVDA currently works in single-channel mode."));
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.Nvda.Line2", "Automatic reading and manual browse share the same channel."));
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.Nvda.Line3", "BaoYi main/independent channel settings do not apply under this interface."));
                    break;
                default:
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.Auto.Line1", "Auto mode prefers BaoYi YueTing new interface, then falls back to the older BaoYi interface and other available interfaces."));
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.Auto.Line2", "If a multi-channel interface is selected at runtime, the main channel option and independent channel name are used."));
                    builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.Auto.Line3", "If a single-channel interface such as NVDA is selected, it works in single-channel mode."));
                    break;
            }

            builder.AppendLine();
            builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.SharedChannel",
                "Supported interfaces use MyCodex when the channel name is empty. New targets copy This PC's channel name; you can change each target separately afterward. Identical names share reader settings and speech; stopping that channel may affect other targets. Closing a target only releases its connection."));
            builder.AppendLine();
            builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.ScenariosTitle", "Usage scenarios:"));
            builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.Scenarios.AutoRead", "Automatic reading is used for new messages and important status announcements."));
            builder.AppendLine(UiLocalization.Text("ScreenReaderSettingsForm.Description.Scenarios.ManualBrowse", "Manual browse is used with Alt+Arrow keys to read messages and terminal output line by line."));
            TextBoxCaretHelper.SetTextAndMoveToStart(_descriptionTextBox, builder.ToString().Trim());
        }

        private void SelectProvider(ScreenReaderProviderKind provider)
        {
            provider = NormalizeProvider(provider);
            for (var i = 0; i < _providerComboBox.Items.Count; i++)
            {
                var item = _providerComboBox.Items[i] as ProviderItem;
                if (item != null && item.Provider == provider)
                {
                    _providerComboBox.SelectedIndex = i;
                    return;
                }
            }

            _providerComboBox.SelectedIndex = 0;
        }

        private ScreenReaderProviderKind GetSelectedProvider()
        {
            var item = _providerComboBox.SelectedItem as ProviderItem;
            return item != null ? item.Provider : ScreenReaderProviderKind.Auto;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            ApplySelection();
            DialogResult = DialogResult.OK;
            Close();
        }

        internal void ApplySelection()
        {
            SelectedProvider = GetSelectedProvider();
            ManualBrowseUseReaderMainChannel =
                _manualBrowseMainChannelCheckBox.Checked &&
                SelectedProvider != ScreenReaderProviderKind.Nvda;
            ScreenReaderIndependentChannelName = NormalizeOptionalText(_independentChannelNameTextBox.Text);
        }

        private static string NormalizeOptionalText(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static string GetProviderText(ScreenReaderProviderKind provider)
        {
            switch (provider)
            {
                case ScreenReaderProviderKind.ByCtrl:
                    return UiLocalization.Text("ScreenReaderSettingsForm.Provider.ByCtrl", "BaoYi YueTing");
                case ScreenReaderProviderKind.BaoYiWebApi:
                    return UiLocalization.Text("ScreenReaderSettingsForm.Provider.BaoYiWebApi", "保益悦听新接口");
                case ScreenReaderProviderKind.Zdsr:
                    return UiLocalization.Text("ScreenReaderSettingsForm.Provider.Zdsr", "ZDSR");
                case ScreenReaderProviderKind.Nvda:
                    return UiLocalization.Text("ScreenReaderSettingsForm.Provider.Nvda", "NVDA");
                default:
                    return UiLocalization.Text("ScreenReaderSettingsForm.Provider.Auto", "Auto");
            }
        }

        private static ScreenReaderProviderKind NormalizeProvider(ScreenReaderProviderKind provider)
        {
            if ((int)provider == 1)
            {
                return ScreenReaderProviderKind.ByCtrl;
            }

            switch (provider)
            {
                case ScreenReaderProviderKind.Auto:
                case ScreenReaderProviderKind.ByCtrl:
                case ScreenReaderProviderKind.BaoYiWebApi:
                case ScreenReaderProviderKind.Zdsr:
                case ScreenReaderProviderKind.Nvda:
                    return provider;
                default:
                    return ScreenReaderProviderKind.Auto;
            }
        }

        private sealed class ProviderItem
        {
            public ProviderItem(ScreenReaderProviderKind provider, string text)
            {
                Provider = provider;
                Text = text;
            }

            public ScreenReaderProviderKind Provider { get; }

            public string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
