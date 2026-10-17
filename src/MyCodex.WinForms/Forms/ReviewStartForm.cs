using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class ReviewStartForm : Form
    {
        private readonly ComboBox _runModeComboBox;
        private readonly ComboBox _targetComboBox;
        private readonly TextBox _targetInput;
        private readonly Label _targetInputLabel;
        private readonly Label _deliveryNotice;
        private readonly Button _startButton;
        private readonly Button _cancelButton;

        public ReviewStartForm()
        {
            Text = UiLocalization.Text("ReviewStartForm.Title", "Start Review");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(620, 440);
            MinimumSize = Size;
            UiTheme.ApplyForm(this);

            var titleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                Text = UiLocalization.Text("ReviewStartForm.Heading", "Choose what to review in this workspace"),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var tipLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                Text = UiLocalization.Text(
                    "ReviewStartForm.Tip",
                    "Choose uncommitted changes, a base branch, a commit, or custom instructions. Review uses the selected session's workspace."),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false,
                TabIndex = 0
            };

            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };

            var formTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 4,
                TabStop = false,
                TabIndex = 0
            };
            formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var targetLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("ReviewStartForm.Label.Target", "Target(&T)"),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false,
                TabIndex = 0
            };

            _targetComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("ReviewStartForm.Accessible.Target", "Review target"),
                TabIndex = 1
            };
            UiTheme.ApplyComboBox(_targetComboBox);
            _targetComboBox.Items.Add(UiLocalization.Text("ReviewStartForm.Value.TargetCurrentWorkspace", "Uncommitted changes"));
            _targetComboBox.Items.Add(UiLocalization.Text("ReviewStartForm.Target.BaseBranch", "Base branch"));
            _targetComboBox.Items.Add(UiLocalization.Text("ReviewStartForm.Target.Commit", "Commit"));
            _targetComboBox.Items.Add(UiLocalization.Text("ReviewStartForm.Target.Custom", "Custom instructions"));
            _targetInputLabel = new Label { Dock = DockStyle.Top, AutoSize = true, TabStop = false, TabIndex = 2 };
            _targetInput = new TextBox { Dock = DockStyle.Fill, TabIndex = 3, Height = 28 };
            UiTheme.ApplyTextBox(_targetInput, false, false);

            var runModeLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("ReviewStartForm.Label.Mode", "Mode(&M)"),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false,
                TabIndex = 4
            };

            _runModeComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("ReviewStartForm.Accessible.Mode", "Review Mode"),
                TabIndex = 5
            };
            UiTheme.ApplyComboBox(_runModeComboBox);
            _runModeComboBox.Items.Add(UiLocalization.Text("ReviewStartForm.Mode.NewSessionRecommended", "Run in new session (recommended)"));
            _runModeComboBox.Items.Add(UiLocalization.Text("ReviewStartForm.Mode.CurrentSession", "Run in current session"));
            _runModeComboBox.SelectedIndex = 0;
            _deliveryNotice = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                TabStop = false,
                Text = UiLocalization.Text("ReviewStartForm.DetachedNotice",
                    "Creates a new session with the selected directory, model, reasoning effort and permissions, then runs official review in it. Source conversation history is not copied."),
                ForeColor = UiTheme.MutedTextColor
            };
            _runModeComboBox.SelectedIndexChanged += (sender, args) => _deliveryNotice.Visible = RunInNewSession;
            _targetComboBox.SelectedIndexChanged += (sender, args) => UpdateTargetInput();
            _targetComboBox.SelectedIndex = 0;

            formTable.Controls.Add(targetLabel, 0, 0);
            formTable.Controls.Add(_targetComboBox, 1, 0);
            formTable.Controls.Add(_targetInputLabel, 0, 1);
            formTable.Controls.Add(_targetInput, 1, 1);
            formTable.Controls.Add(runModeLabel, 0, 2);
            formTable.Controls.Add(_runModeComboBox, 1, 2);
            formTable.Controls.Add(_deliveryNotice, 0, 3);
            formTable.SetColumnSpan(_deliveryNotice, 2);

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(0, 12, 0, 0),
                TabStop = false,
                TabIndex = 1
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

            _startButton = new Button
            {
                Text = UiLocalization.Text("ReviewStartForm.Button.Start", "Start Review(&S)"),
                AccessibleName = UiLocalization.Text("ReviewStartForm.Accessible.Start", "Start Review"),
                Width = 96,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                UseMnemonic = true,
                TabIndex = 0
            };
            UiTheme.ApplyButton(_startButton, UiGlyph.Search);
            _startButton.Click += StartButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("ReviewStartForm.Button.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("ReviewStartForm.Accessible.Cancel", "Cancel"),
                Width = 96,
                Height = 32,
                UseMnemonic = true,
                TabIndex = 1
            };
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);
            _cancelButton.Click += CancelButton_Click;

            buttonPanel.Controls.Add(_startButton);
            buttonPanel.Controls.Add(_cancelButton);
            footerPanel.Controls.Add(buttonPanel);

            contentPanel.Controls.Add(footerPanel);
            contentPanel.Controls.Add(formTable);
            contentPanel.Controls.Add(tipLabel);
            contentPanel.Controls.Add(titleLabel);

            Controls.Add(contentPanel);

            AcceptButton = _startButton;
            CancelButton = _cancelButton;
            Shown += ReviewStartForm_Shown;
        }

        public bool RunInNewSession
        {
            get { return _runModeComboBox.SelectedIndex == 0; }
        }

        public AppServerReviewTarget SelectedTarget { get; private set; }

        internal AppServerReviewTarget BuildValidatedTarget()
        {
            var value = _targetInput.Text.Trim();
            AppServerReviewTarget target;
            switch (_targetComboBox.SelectedIndex)
            {
                case 0: target = AppServerReviewTarget.CreateUncommittedChanges(); break;
                case 1: target = AppServerReviewTarget.CreateBaseBranch(value); break;
                case 2: target = AppServerReviewTarget.CreateCommit(value, null); break;
                case 3: target = AppServerReviewTarget.CreateCustom(value); break;
                default: throw new InvalidOperationException(UiLocalization.Text("ReviewStartForm.Message.SelectTarget", "Please choose a review target."));
            }
            target.Validate();
            return target;
        }

        private void UpdateTargetInput()
        {
            var kind = _targetComboBox.SelectedIndex;
            _targetInput.Visible = _targetInputLabel.Visible = kind > 0;
            _targetInput.Multiline = kind == 3;
            _targetInput.AcceptsReturn = kind == 3;
            _targetInput.ScrollBars = kind == 3 ? ScrollBars.Vertical : ScrollBars.None;
            _targetInput.Height = IsHandleCreated ? UiTheme.Scale(kind == 3 ? 96 : 28) : kind == 3 ? 96 : 28;
            _targetInputLabel.Text = kind == 1
                ? UiLocalization.Text("ReviewStartForm.Label.Branch", "Base branch(&B)")
                : kind == 2 ? UiLocalization.Text("ReviewStartForm.Label.Commit", "Commit hash(&H)")
                : UiLocalization.Text("ReviewStartForm.Label.Instructions", "Instructions(&I)");
            _targetInput.AccessibleName = _targetComboBox.Items[Math.Max(0, kind)].ToString();
            _targetInput.Clear();
        }

        private void ReviewStartForm_Shown(object sender, EventArgs e)
        {
            _targetComboBox.Focus();
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            if (_runModeComboBox.SelectedIndex < 0)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("ReviewStartForm.Message.SelectMode", "Please choose a review mode."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _runModeComboBox.Focus();
                return;
            }

            try
            {
                SelectedTarget = BuildValidatedTarget();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _targetInput.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
