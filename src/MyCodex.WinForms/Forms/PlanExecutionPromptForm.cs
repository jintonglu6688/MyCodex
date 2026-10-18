using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class PlanExecutionPromptForm : Form
    {
        private readonly TextBox _planTextBox;
        private readonly Button _executeButton;
        private readonly Button _continueButton;

        public PlanExecutionPromptForm(string planContent)
        {
            Text = UiLocalization.Text("PlanExecutionPromptForm.Title", "Plan Ready");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 360);
            Size = new Size(720, 520);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            var promptLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text(
                    "PlanExecutionPromptForm.Prompt",
                    "Review the generated plan below, then choose what to do next."),
                Margin = new Padding(0, 0, 0, UiTheme.SectionGap)
            };
            UiTheme.ApplyLabel(promptLabel, true);

            _planTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("PlanExecutionPromptForm.Accessible.PlanContent", "Generated plan"),
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                TabIndex = 0,
                Text = string.IsNullOrWhiteSpace(planContent) ? string.Empty : planContent.Trim(),
                HideSelection = false
            };
            UiTheme.ApplyTextBox(_planTextBox, false, true);

            var contentPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            contentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            contentPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            contentPanel.Controls.Add(promptLabel, 0, 0);
            contentPanel.Controls.Add(_planTextBox, 0, 1);

            var buttonPanel = UiControlFactory.CreateDialogButtonBar();

            _continueButton = new Button
            {
                Text = UiLocalization.Text("PlanExecutionPromptForm.Option.ContinuePlanning", "Continue Discussion(&C)"),
                AccessibleName = UiLocalization.Text("PlanExecutionPromptForm.Accessible.ContinuePlanning", "Continue discussion"),
                AutoSize = true,
                TabIndex = 2,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_continueButton, UiGlyph.Cancel);
            _continueButton.Click += delegate
            {
                ShouldExecutePlan = false;
                DialogResult = DialogResult.Cancel;
                Close();
            };

            _executeButton = new Button
            {
                Text = UiLocalization.Text("PlanExecutionPromptForm.Option.Execute", "Execute Plan(&E)"),
                AccessibleName = UiLocalization.Text("PlanExecutionPromptForm.Accessible.Execute", "Execute plan"),
                AutoSize = true,
                TabIndex = 1
            };
            UiTheme.ApplyDialogButton(_executeButton, UiGlyph.Ok);
            _executeButton.Click += delegate
            {
                ShouldExecutePlan = true;
                DialogResult = DialogResult.OK;
                Close();
            };

            buttonPanel.Controls.Add(_continueButton);
            buttonPanel.Controls.Add(_executeButton);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(contentPanel, 0, 0);
            root.Controls.Add(buttonPanel, 0, 1);
            Controls.Add(root);

            AcceptButton = _executeButton;
            CancelButton = _continueButton;
        }

        public bool ShouldExecutePlan { get; private set; }

        public bool WasExternallyResolved { get; private set; }

        public void CloseAsExternallyResolved(bool shouldExecutePlan)
        {
            if (IsDisposed)
            {
                return;
            }

            WasExternallyResolved = true;
            ShouldExecutePlan = shouldExecutePlan;
            DialogResult = shouldExecutePlan ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _planTextBox.Focus();
            _planTextBox.SelectionStart = 0;
            _planTextBox.SelectionLength = 0;
        }
    }
}
