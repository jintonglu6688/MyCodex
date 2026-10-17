using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class StartupModeOnboardingForm : Form
    {
        private readonly RadioButton _officeRadioButton;
        private readonly RadioButton _codingRadioButton;
        private readonly Button _okButton;

        public StartupModeOnboardingForm(AppInteractionMode defaultMode)
        {
            Text = UiLocalization.Text("StartupModeOnboardingForm.Title", "Choose Startup Mode");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(520, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _officeRadioButton = new RadioButton
            {
                Text = UiLocalization.Text("StartupModeOnboardingForm.Option.OfficeMode", "Office Mode(&F)"),
                AccessibleName = UiLocalization.Text("StartupModeOnboardingForm.Accessible.OfficeMode", "Office Mode"),
                AutoSize = true,
                Dock = DockStyle.Top,
                TabIndex = 0
            };
            _codingRadioButton = new RadioButton
            {
                Text = UiLocalization.Text("StartupModeOnboardingForm.Option.CodingMode", "Coding Mode(&C)"),
                AccessibleName = UiLocalization.Text("StartupModeOnboardingForm.Accessible.CodingMode", "Coding Mode"),
                AutoSize = true,
                Dock = DockStyle.Top,
                TabIndex = 1
            };
            _okButton = new Button
            {
                Text = UiLocalization.Text("Common.OK", "&OK"),
                AccessibleName = UiLocalization.Text("Common.Accessible.OK", "OK"),
                AutoSize = true,
                DialogResult = DialogResult.OK,
                TabIndex = 2
            };
            UiTheme.ApplyDialogButton(_okButton, UiGlyph.Ok);
            _okButton.Click += OkButton_Click;

            if (defaultMode == AppInteractionMode.Office)
            {
                _officeRadioButton.Checked = true;
                SelectedMode = AppInteractionMode.Office;
            }
            else
            {
                _codingRadioButton.Checked = true;
                SelectedMode = AppInteractionMode.Coding;
            }

            BuildLayout();
            AcceptButton = _okButton;
        }

        public AppInteractionMode SelectedMode { get; private set; }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var titleLabel = new Label
            {
                Text = UiLocalization.Text("StartupModeOnboardingForm.Header", "Which mode should MyCodex open by default?"),
                AccessibleName = UiLocalization.Text("StartupModeOnboardingForm.Header", "Which mode should MyCodex open by default?"),
                Dock = DockStyle.Top,
                AutoSize = true,
                TabStop = false
            };
            titleLabel.Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold);
            UiTheme.ApplyLabel(titleLabel, true);

            var choicesPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                TabStop = false
            };
            choicesPanel.Controls.Add(_officeRadioButton);
            choicesPanel.Controls.Add(_codingRadioButton);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                TabStop = false
            };
            buttonPanel.Controls.Add(_okButton);

            root.Controls.Add(titleLabel, 0, 0);
            root.Controls.Add(choicesPanel, 0, 1);
            root.Controls.Add(buttonPanel, 0, 2);
            Controls.Add(root);
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            SelectedMode = _officeRadioButton.Checked ? AppInteractionMode.Office : AppInteractionMode.Coding;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
