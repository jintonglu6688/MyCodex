using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public class ToolUserInputForm : Form
    {
        private readonly List<QuestionEditor> _editors;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly AccessibleStaticTextControl _confirmationControl;

        public ToolUserInputForm(IReadOnlyList<AppServerToolUserInputQuestion> questions)
            : this(questions, null, null)
        {
        }

        public ToolUserInputForm(IReadOnlyList<AppServerToolUserInputQuestion> questions, string title, string headerText)
            : this(questions, title, headerText, false)
        {
        }

        internal ToolUserInputForm(IReadOnlyList<AppServerToolUserInputQuestion> questions, string title, string headerText, bool confirmation)
        {
            if (questions == null)
            {
                throw new ArgumentNullException(nameof(questions));
            }

            _editors = new List<QuestionEditor>();

            Text = string.IsNullOrWhiteSpace(title)
                ? UiLocalization.Text("ToolUserInputForm.Title", "Plan Mode")
                : title;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(620, 420);
            Size = new Size(760, 560);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12),
                TabStop = false
            };
            root.RowStyles.Add(confirmation ? new RowStyle(SizeType.Absolute, 120F) : new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var titleLabel = new Label
            {
                Text = string.IsNullOrWhiteSpace(headerText)
                    ? UiLocalization.Text("ToolUserInputForm.Header", "The tool needs additional input before it can continue.")
                    : headerText,
                AccessibleName = UiLocalization.Text("ToolUserInputForm.Accessible.Header", "Prompt"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8),
                TabStop = false
            };

            var scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                MinimumSize = new Size(0, 120),
                TabStop = false
            };

            var questionsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = Math.Max(1, questions.Count),
                TabStop = false
            };
            questionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            for (var i = 0; i < questions.Count; i++)
            {
                questionsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var editor = new QuestionEditor(questions[i], 10 + (i * 100));
                _editors.Add(editor);
                questionsPanel.Controls.Add(editor.Container, 0, i);
            }

            scrollPanel.Controls.Add(questionsPanel);

            var buttonPanel = UiControlFactory.CreateDialogButtonBar();

            _okButton = new Button
            {
                Text = UiLocalization.Text("ToolUserInputForm.Button.OK", "&OK"),
                AccessibleName = UiLocalization.Text("ToolUserInputForm.Accessible.OK", "OK"),
                AutoSize = true,
                TabIndex = 2000
            };
            _okButton.Click += OkButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("ToolUserInputForm.Button.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("ToolUserInputForm.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = 2001,
                DialogResult = DialogResult.Cancel
            };

            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_okButton);

            if (confirmation)
            {
                UiTheme.ApplyForm(this);
                _confirmationControl = new AccessibleStaticTextControl
                {
                    Dock = DockStyle.Fill,
                    Text = titleLabel.Text,
                    AccessibleName = titleLabel.Text,
                    AccessibleRole = AccessibleRole.StaticText,
                    BackColor = UiTheme.WindowBackColor,
                    ForeColor = UiTheme.TextColor,
                    TabIndex = 0,
                    Margin = new Padding(0, 0, 0, 8)
                };
                root.Controls.Add(_confirmationControl, 0, 0);
                titleLabel.Dispose();
            }
            else root.Controls.Add(titleLabel, 0, 0);
            root.Controls.Add(scrollPanel, 0, 1);
            root.Controls.Add(buttonPanel, 0, 2);

            Controls.Add(root);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }

        public Dictionary<string, string[]> Answers { get; private set; }

        public bool WasExternallyResolved { get; private set; }

        public void CloseAsExternallyResolved()
        {
            WasExternallyResolved = true;
            DialogResult = DialogResult.Abort;
            Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_confirmationControl != null)
            {
                _confirmationControl.Focus();
                return;
            }
            if (_editors.Count > 0)
            {
                _editors[0].Focus();
            }
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            var answers = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var editor in _editors)
            {
                string errorMessage;
                string[] values;
                if (!editor.TryGetAnswers(out values, out errorMessage))
                {
                    MessageBox.Show(this, errorMessage, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    editor.Focus();
                    return;
                }

                answers[editor.QuestionId] = values;
            }

            Answers = answers;
            DialogResult = DialogResult.OK;
            Close();
        }

        private sealed class QuestionEditor
        {
            private const int LongDescriptionThreshold = 160;

            private readonly AppServerToolUserInputQuestion _question;
            private readonly TextBox _optionDescriptionTextBox;
            private readonly ComboBox _optionComboBox;
            private readonly TextBox _textBox;
            private readonly TextBox _questionDescriptionTextBox;

            public QuestionEditor(AppServerToolUserInputQuestion question, int baseTabIndex)
            {
                _question = question ?? throw new ArgumentNullException(nameof(question));

                Container = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    ColumnCount = 1,
                    RowCount = 6,
                    Margin = new Padding(0, 0, 0, 10),
                    Padding = new Padding(0),
                    TabStop = false
                };
                Container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                Container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Container.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var title = string.IsNullOrWhiteSpace(question.Header)
                    ? UiLocalization.Text("ToolUserInputForm.Question.DefaultHeader", "Input")
                    : question.Header;
                var questionText = string.IsNullOrWhiteSpace(question.Question) ? title : question.Question;
                var showHeader = !string.IsNullOrWhiteSpace(question.Header) &&
                    !string.Equals(question.Header.Trim(), questionText.Trim(), StringComparison.Ordinal);

                if (showHeader)
                {
                    var headerLabel = CreateTextLabel(question.Header.Trim(), new Padding(0, 0, 0, 3));
                    Container.Controls.Add(headerLabel, 0, 0);
                }

                var questionLabel = CreateTextLabel(questionText, new Padding(0, 0, 0, 6));
                Container.Controls.Add(questionLabel, 0, 1);

                var questionDescription = question.Description == null ? string.Empty : question.Description.Trim();
                var hasQuestionDescription = !string.IsNullOrWhiteSpace(questionDescription);
                if (hasQuestionDescription)
                {
                    _questionDescriptionTextBox = new TextBox
                    {
                        Text = questionDescription,
                        Multiline = true,
                        ReadOnly = true,
                        ScrollBars = ScrollBars.Vertical,
                        Dock = DockStyle.Top,
                        Height = GetDescriptionHeight(questionDescription),
                        AccessibleName = UiLocalization.Text("ToolUserInputForm.Accessible.Details", "Details"),
                        TabStop = true,
                        TabIndex = baseTabIndex
                    };
                    UiTheme.ApplyTextBox(_questionDescriptionTextBox, false, true);
                    Container.Controls.Add(_questionDescriptionTextBox, 0, 2);
                }

                var optionItems = BuildOptionItems(question);
                var hasOptionPicker = optionItems.Count > 0;
                if (hasOptionPicker)
                {
                    _optionComboBox = new ComboBox
                    {
                        Dock = DockStyle.Top,
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        AccessibleName = questionText,
                        TabIndex = hasQuestionDescription ? baseTabIndex + 1 : baseTabIndex
                    };
                    _optionComboBox.SelectedIndexChanged += OptionComboBox_SelectedIndexChanged;

                    foreach (var option in optionItems)
                    {
                        _optionComboBox.Items.Add(option);
                    }

                    Container.Controls.Add(_optionComboBox, 0, 3);
                }

                var inputTabIndex = hasOptionPicker
                    ? _optionComboBox.TabIndex + 1
                    : hasQuestionDescription ? baseTabIndex + 1 : baseTabIndex;
                _textBox = new TextBox
                {
                    Dock = DockStyle.Top,
                    AccessibleName = questionText,
                    UseSystemPasswordChar = question.IsSecret,
                    Visible = !hasOptionPicker,
                    Enabled = !hasOptionPicker,
                    TabIndex = inputTabIndex
                };
                Container.Controls.Add(_textBox, 0, 4);

                _optionDescriptionTextBox = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Top,
                    Height = 48,
                    AccessibleName = UiLocalization.Text("ToolUserInputForm.Accessible.OptionDetails", "Option details"),
                    Margin = new Padding(0, 4, 0, 0),
                    TabStop = false,
                    Visible = false,
                    Enabled = false,
                    TabIndex = inputTabIndex + 1
                };
                UiTheme.ApplyTextBox(_optionDescriptionTextBox, false, true);
                Container.Controls.Add(_optionDescriptionTextBox, 0, 5);

                if (_optionComboBox != null && _optionComboBox.Items.Count > 0)
                {
                    _optionComboBox.SelectedIndex = 0;
                }
            }

            public TableLayoutPanel Container { get; }

            public string QuestionId
            {
                get { return _question.Id; }
            }

            public void Focus()
            {
                if (_questionDescriptionTextBox != null)
                {
                    _questionDescriptionTextBox.Focus();
                    return;
                }

                if (_optionComboBox != null)
                {
                    _optionComboBox.Focus();
                    return;
                }

                _textBox.Focus();
            }

            public bool TryGetAnswers(out string[] answers, out string errorMessage)
            {
                if (_optionComboBox != null)
                {
                    var selected = _optionComboBox.SelectedItem as OptionItem;
                    if (selected == null)
                    {
                        answers = null;
                        errorMessage = UiLocalization.Text("ToolUserInputForm.Error.CompleteAll", "Please complete all input items first.");
                        return false;
                    }

                    return TryBuildOptionAnswer(selected, out answers, out errorMessage);
                }

                var value = (_textBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    answers = null;
                    errorMessage = UiLocalization.Text("ToolUserInputForm.Error.CompleteAll", "Please complete all input items first.");
                    return false;
                }

                answers = new[] { value };
                errorMessage = null;
                return true;
            }

            private static List<OptionItem> BuildOptionItems(AppServerToolUserInputQuestion question)
            {
                var items = new List<OptionItem>();
                if (question.Options != null)
                {
                    foreach (var option in question.Options)
                    {
                        if (option == null)
                        {
                            continue;
                        }

                        items.Add(new OptionItem(option.Label, option.Value, option.Description, false));
                    }
                }

                if (question.IsOther)
                {
                    items.Add(new OptionItem(
                        UiLocalization.Text("ToolUserInputForm.Option.Other", "Other"),
                        null,
                        UiLocalization.Text("ToolUserInputForm.Option.OtherDescription", "Manually enter another value."),
                        true));
                }

                return items;
            }

            private bool TryBuildOptionAnswer(OptionItem selected, out string[] answers, out string errorMessage)
            {
                if (selected.IsOther)
                {
                    var customValue = (_textBox.Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(customValue))
                    {
                        answers = null;
                        errorMessage = UiLocalization.Text("ToolUserInputForm.Error.OtherRequired", "Please enter a value for Other.");
                        return false;
                    }

                    answers = new[] { customValue };
                    errorMessage = null;
                    return true;
                }

                answers = new[] { string.IsNullOrWhiteSpace(selected.Value) ? selected.Label : selected.Value };
                errorMessage = null;
                return true;
            }

            private void OptionComboBox_SelectedIndexChanged(object sender, EventArgs e)
            {
                UpdateSelectedOption(_optionComboBox.SelectedItem as OptionItem);
            }

            private void UpdateSelectedOption(OptionItem selected)
            {
                if (selected == null)
                {
                    _optionDescriptionTextBox.Text = string.Empty;
                    _optionDescriptionTextBox.Visible = false;
                    _optionDescriptionTextBox.Enabled = false;
                    _optionDescriptionTextBox.TabStop = false;
                    _textBox.Visible = false;
                    _textBox.Enabled = false;
                    return;
                }

                var optionDescription = selected.Description ?? string.Empty;
                _optionDescriptionTextBox.Text = string.Empty;
                _optionDescriptionTextBox.Visible = false;
                _optionDescriptionTextBox.Enabled = false;
                _optionDescriptionTextBox.TabStop = false;
                var requiresOther = selected.IsOther;
                _textBox.Visible = requiresOther;
                _textBox.Enabled = requiresOther;
                _textBox.AccessibleName = requiresOther
                    ? UiLocalization.Text("ToolUserInputForm.Accessible.OtherInput", "New idea")
                    : _question.Question;
                if (!requiresOther)
                {
                    _textBox.Text = string.Empty;
                }
            }

            private static Label CreateTextLabel(string text, Padding margin)
            {
                return new Label
                {
                    Text = text,
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    MaximumSize = new Size(720, 0),
                    Margin = margin,
                    TabStop = false,
                    UseMnemonic = false
                };
            }

            private static bool IsLongDescription(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                return text.Length >= LongDescriptionThreshold ||
                    text.IndexOf('\n') >= 0 ||
                    text.IndexOf('\r') >= 0;
            }

            private static int GetDescriptionHeight(string text)
            {
                return IsLongDescription(text) ? 92 : 48;
            }

            private sealed class OptionItem
            {
                public OptionItem(string label, string value, string description, bool isOther)
                {
                    Label = label ?? string.Empty;
                    Value = value;
                    Description = description ?? string.Empty;
                    IsOther = isOther;
                }

                public string Label { get; }

                public string Value { get; }

                public string Description { get; }

                public bool IsOther { get; }

                public override string ToString()
                {
                    if (!string.IsNullOrWhiteSpace(Description))
                    {
                        return Label + " - " + Description;
                    }

                    return Label;
                }
            }
        }
    }
}
