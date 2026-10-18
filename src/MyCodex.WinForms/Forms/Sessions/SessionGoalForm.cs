using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class SessionGoalForm : Form
    {
        private const int MinimumObjectiveLength = 6;
        private const int MaximumObjectiveLength = 4000;
        private const decimal TokenBudgetUnit = 10000M;
        private const decimal MaximumTokenBudgetUnits = 1000M;
        private readonly TextBox _objectiveTextBox;
        private readonly TextBox _tokenBudgetTextBox;
        private readonly Button _confirmButton;
        private readonly Button _cancelButton;

        public SessionGoalForm(string initialObjective, long? initialTokenBudget)
        {
            Text = UiLocalization.Text("SessionGoalForm.Title", "Set Goal");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(600, 420);
            Size = new Size(720, 520);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);

            var headingLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                Text = UiLocalization.Text("SessionGoalForm.Heading", "Set the current session goal"),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var tipLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 56,
                Text = UiLocalization.Text("SessionGoalForm.Tip", "The goal belongs to the App Server thread and can be restored after restarting MyCodex when the runtime supports goals."),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            _objectiveTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("SessionGoalForm.Accessible.Objective", "Objective"),
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                TabIndex = 0,
                Text = initialObjective ?? string.Empty,
                HideSelection = false
            };
            UiTheme.ApplyTextBox(_objectiveTextBox, false, true);

            var objectiveLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = UiLocalization.Text("SessionGoalForm.Label.Objective", "Objective(&O)"),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var tokenBudgetPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 82,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            var tokenBudgetLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = UiLocalization.Text("SessionGoalForm.Label.TokenBudget", "Token budget in 10k units(&T), optional"),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            _tokenBudgetTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("SessionGoalForm.Accessible.TokenBudget", "Token budget"),
                Dock = DockStyle.Top,
                Height = 28,
                TabIndex = 1,
                Text = initialTokenBudget.HasValue && initialTokenBudget.Value > 0
                    ? FormatTokenBudgetUnits(initialTokenBudget.Value)
                    : string.Empty,
                HideSelection = false
            };
            UiTheme.ApplyTextBox(_tokenBudgetTextBox, false, true);

            var tokenBudgetTipLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = UiLocalization.Text("SessionGoalForm.TokenBudget.Tip", "Leave empty for no explicit budget. Example: 5 means 50,000 tokens; 0.5 means 5,000 tokens."),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            tokenBudgetPanel.Controls.Add(tokenBudgetTipLabel);
            tokenBudgetPanel.Controls.Add(_tokenBudgetTextBox);
            tokenBudgetPanel.Controls.Add(tokenBudgetLabel);

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(0, 12, 0, 0),
                TabStop = false
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 208,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            _confirmButton = new Button
            {
                Text = UiLocalization.Text("SessionGoalForm.Button.Set", "Set(&S)"),
                AccessibleName = UiLocalization.Text("SessionGoalForm.Accessible.Set", "Set"),
                Width = 96,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                UseMnemonic = true
            };
            UiTheme.ApplyButton(_confirmButton, UiGlyph.Ok);
            _confirmButton.Click += ConfirmButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("SessionGoalForm.Button.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("SessionGoalForm.Accessible.Cancel", "Cancel"),
                Width = 96,
                Height = 32,
                UseMnemonic = true
            };
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);
            _cancelButton.Click += CancelButton_Click;

            buttonPanel.Controls.Add(_confirmButton);
            buttonPanel.Controls.Add(_cancelButton);
            footerPanel.Controls.Add(buttonPanel);

            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            contentPanel.Controls.Add(_objectiveTextBox);
            contentPanel.Controls.Add(tokenBudgetPanel);
            contentPanel.Controls.Add(objectiveLabel);
            contentPanel.Controls.Add(tipLabel);
            contentPanel.Controls.Add(headingLabel);
            contentPanel.Controls.Add(footerPanel);
            Controls.Add(contentPanel);

            AcceptButton = _confirmButton;
            CancelButton = _cancelButton;
            Shown += SessionGoalForm_Shown;
        }

        public string Objective
        {
            get { return (_objectiveTextBox.Text ?? string.Empty).Trim(); }
        }

        public long? TokenBudget
        {
            get
            {
                decimal units;
                return decimal.TryParse((_tokenBudgetTextBox.Text ?? string.Empty).Trim(), out units)
                    ? (long?)decimal.ToInt64(decimal.Ceiling(units * TokenBudgetUnit))
                    : null;
            }
        }

        private void SessionGoalForm_Shown(object sender, EventArgs e)
        {
            _objectiveTextBox.Focus();
            _objectiveTextBox.SelectAll();
        }

        private void ConfirmButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Objective))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("SessionGoalForm.Message.EnterObjective", "Please enter a goal objective."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _objectiveTextBox.Focus();
                return;
            }

            if (Objective.Length < MinimumObjectiveLength)
            {
                MessageBox.Show(
                    this,
                    string.Format(
                        UiLocalization.Text("SessionGoalForm.Message.ObjectiveTooShort.Template", "Please enter at least {0} characters for the goal."),
                        MinimumObjectiveLength),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _objectiveTextBox.Focus();
                return;
            }

            if (Objective.Length > MaximumObjectiveLength)
            {
                MessageBox.Show(
                    this,
                    string.Format(
                        UiLocalization.Text("SessionGoalForm.Message.ObjectiveTooLong.Template", "Please keep the goal within {0} characters."),
                        MaximumObjectiveLength),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _objectiveTextBox.Focus();
                return;
            }

            var tokenBudgetText = (_tokenBudgetTextBox.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(tokenBudgetText))
            {
                decimal tokenBudgetUnits;
                if (!decimal.TryParse(tokenBudgetText, out tokenBudgetUnits) ||
                    tokenBudgetUnits <= 0 ||
                    tokenBudgetUnits > MaximumTokenBudgetUnits)
                {
                    MessageBox.Show(
                        this,
                        string.Format(
                            UiLocalization.Text("SessionGoalForm.Message.InvalidTokenBudget.Template", "Token budget must be a positive number no larger than {0}."),
                            MaximumTokenBudgetUnits),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    _tokenBudgetTextBox.Focus();
                    _tokenBudgetTextBox.SelectAll();
                    return;
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private static string FormatTokenBudgetUnits(long tokenBudget)
        {
            var units = tokenBudget / TokenBudgetUnit;
            return units == decimal.Truncate(units)
                ? units.ToString("0")
                : units.ToString("0.####");
        }
    }
}
