using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class AsyncInputRequestEventArgs : EventArgs
    {
        internal AsyncInputRequestEventArgs(string requestKey)
        {
            RequestKey = requestKey;
        }

        internal string RequestKey { get; }
    }

    internal sealed class AsyncInputRequestForm : Form
    {
        private const int RadioOptionLimit = 4;

        private readonly AsyncInputRequestQueue _queue;
        private readonly ComboBox _requestPicker;
        private readonly Panel _questionHost;
        private readonly Label _sourceLabel;
        private readonly Label _statusLabel;
        private readonly Button _sendButton;
        private readonly Button _ignoreButton;
        private readonly Button _laterButton;
        private readonly List<QuestionEditor> _editors = new List<QuestionEditor>();
        private string _currentKey;
        private bool _refreshing;

        internal AsyncInputRequestForm(AsyncInputRequestQueue queue)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));

            Text = UiLocalization.Text("AsyncInputForm.Title", "Pending questions");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 440);
            Size = new Size(780, 600);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _requestPicker = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("AsyncInputForm.Accessible.RequestPicker", "Pending request"),
                TabIndex = 0,
                Margin = new Padding(0, 0, 0, UiTheme.ControlSpacing)
            };
            _requestPicker.SelectedIndexChanged += RequestPicker_SelectedIndexChanged;
            UiTheme.ApplyComboBox(_requestPicker);

            _sourceLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("AsyncInputForm.Accessible.Source", "Request source"),
                TabStop = false,
                Margin = new Padding(0, 0, 0, UiTheme.ControlSpacing)
            };

            _questionHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                TabStop = false
            };

            _statusLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("AsyncInputForm.Accessible.Status", "Status"),
                TabStop = false,
                Margin = new Padding(0, UiTheme.ControlSpacing, 0, UiTheme.ControlSpacing)
            };

            var buttons = UiControlFactory.CreateDialogButtonBar();
            _sendButton = CreateButton(
                UiLocalization.Text("AsyncInputForm.Button.Send", "&Send answers"),
                UiLocalization.Text("AsyncInputForm.Accessible.Send", "Send answers"),
                1000,
                UiGlyph.Send);
            _ignoreButton = CreateButton(
                UiLocalization.Text("AsyncInputForm.Button.Ignore", "&Ignore"),
                UiLocalization.Text("AsyncInputForm.Accessible.Ignore", "Ignore request"),
                1001,
                UiGlyph.Cancel);
            _laterButton = CreateButton(
                UiLocalization.Text("AsyncInputForm.Button.Later", "Answer &later"),
                UiLocalization.Text("AsyncInputForm.Accessible.Later", "Answer later"),
                1002,
                UiGlyph.Info);
            _sendButton.Click += SendButton_Click;
            _ignoreButton.Click += IgnoreButton_Click;
            _laterButton.Click += delegate { Hide(); };
            buttons.Controls.Add(_sendButton);
            buttons.Controls.Add(_ignoreButton);
            buttons.Controls.Add(_laterButton);

            root.Controls.Add(_requestPicker, 0, 0);
            root.Controls.Add(_sourceLabel, 0, 1);
            root.Controls.Add(_questionHost, 0, 2);
            root.Controls.Add(_statusLabel, 0, 3);
            root.Controls.Add(buttons, 0, 4);
            Controls.Add(root);
            AcceptButton = _sendButton;
            CancelButton = _laterButton;
        }

        internal event EventHandler<AsyncInputRequestEventArgs> SubmitRequested;

        internal event EventHandler<AsyncInputRequestEventArgs> IgnoreRequested;

        protected override bool ShowWithoutActivation => true;

        internal void RefreshRequests(string preferredKey = null)
        {
            SaveCurrentAnswers();
            var selectedKey = preferredKey ?? _currentKey;
            _refreshing = true;
            _requestPicker.Items.Clear();
            foreach (var request in _queue.Items)
            {
                _requestPicker.Items.Add(request);
            }

            var selectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(selectedKey))
            {
                for (var i = 0; i < _requestPicker.Items.Count; i++)
                {
                    var request = _requestPicker.Items[i] as PendingAsyncInputRequest;
                    if (request != null && string.Equals(request.Key, selectedKey, StringComparison.Ordinal))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            _requestPicker.SelectedIndex = _requestPicker.Items.Count == 0 ? -1 : selectedIndex;
            _refreshing = false;
            BuildSelectedRequest();
        }

        internal void AddRequest(PendingAsyncInputRequest request)
        {
            if (request == null || _requestPicker.Items.Cast<PendingAsyncInputRequest>().Any(item =>
                string.Equals(item.Key, request.Key, StringComparison.Ordinal)))
            {
                return;
            }

            _requestPicker.Items.Add(request);
            if (_requestPicker.SelectedIndex < 0)
            {
                _requestPicker.SelectedIndex = 0;
            }
        }

        internal void FocusQuestion()
        {
            if (_editors.Count > 0)
            {
                _editors[0].FocusQuestion();
            }
        }

        internal void SetBusy(bool busy, string statusText)
        {
            _requestPicker.Enabled = !busy;
            foreach (var editor in _editors)
            {
                editor.SetInputEnabled(!busy);
            }

            _sendButton.Enabled = !busy && HasAnyAnswer() && !_editors.Any(editor => editor.RequiresOtherAnswer);
            _ignoreButton.Enabled = !busy;
            _laterButton.Enabled = !busy;
            _statusLabel.Text = statusText ?? string.Empty;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnFormClosing(e);
        }

        private Button CreateButton(string text, string accessibleName, int tabIndex, UiGlyph glyph)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                TabIndex = tabIndex,
                Margin = new Padding(UiTheme.ControlSpacing, 0, 0, 0)
            };
            UiTheme.ApplyDialogButton(button, glyph);
            return button;
        }

        private void RequestPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_refreshing)
            {
                return;
            }

            SaveCurrentAnswers();
            BuildSelectedRequest();
        }

        private void BuildSelectedRequest()
        {
            _refreshing = true;
            _questionHost.Controls.Clear();
            _editors.Clear();
            var request = _requestPicker.SelectedItem as PendingAsyncInputRequest;
            _currentKey = request != null ? request.Key : null;
            _sourceLabel.Text = request == null
                ? string.Empty
                : string.Format(
                    UiLocalization.Text("AsyncInputForm.Source.Template", "Source: {0}"),
                    request.SourceName);

            if (request == null)
            {
                _refreshing = false;
                UpdateSendState();
                return;
            }

            var questionsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = request.Source.ToolQuestions.Count,
                TabStop = false
            };
            questionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (var i = 0; i < request.Source.ToolQuestions.Count; i++)
            {
                var editor = new QuestionEditor(request.Source.ToolQuestions[i], i + 1, i * 10 + 1, UpdateSendState);
                string answer;
                request.Answers.TryGetValue(request.Source.ToolQuestions[i].Id, out answer);
                editor.Answer = answer;
                AsyncInputOtherAnswerDraft otherDraft;
                if (request.OtherAnswerDrafts.TryGetValue(editor.QuestionId, out otherDraft))
                {
                    editor.OtherDraft = otherDraft;
                }
                _editors.Add(editor);
                questionsPanel.Controls.Add(editor.Container, 0, i);
            }

            _questionHost.Controls.Add(questionsPanel);
            _statusLabel.Text = string.Empty;
            _refreshing = false;
            UpdateSendState();
        }

        private void SaveCurrentAnswers()
        {
            var request = _queue.Find(_currentKey);
            if (request == null)
            {
                return;
            }

            AsyncInputRequestQueue.SaveAnswers(
                request,
                _editors.ToDictionary(editor => editor.QuestionId, editor => editor.Answer, StringComparer.Ordinal));
            request.OtherAnswerDrafts.Clear();
            foreach (var editor in _editors)
            {
                var draft = editor.OtherDraft;
                if (draft != null) request.OtherAnswerDrafts[editor.QuestionId] = draft;
            }
        }

        private void SendButton_Click(object sender, EventArgs e)
        {
            SaveCurrentAnswers();
            var incomplete = _editors.FirstOrDefault(editor => editor.RequiresOtherAnswer);
            if (incomplete != null || !HasAnyAnswer())
            {
                UpdateSendState();
                incomplete?.FocusAnswer();
                return;
            }

            SubmitRequested?.Invoke(this, new AsyncInputRequestEventArgs(_currentKey));
        }

        private void IgnoreButton_Click(object sender, EventArgs e)
        {
            IgnoreRequested?.Invoke(this, new AsyncInputRequestEventArgs(_currentKey));
        }

        private bool HasAnyAnswer()
        {
            var request = _queue.Find(_currentKey);
            return request != null && request.Answers.Count > 0;
        }

        private void UpdateSendState()
        {
            if (_refreshing)
            {
                return;
            }

            SaveCurrentAnswers();
            var incomplete = _editors.Any(editor => editor.RequiresOtherAnswer);
            _statusLabel.Text = incomplete
                ? UiLocalization.Text("AsyncInputForm.Error.OtherRequired", "Please enter your answer for Other.")
                : string.Empty;
            _sendButton.Enabled = HasAnyAnswer() && !incomplete;
        }

        private sealed class QuestionEditor
        {
            private readonly AccessibleStaticTextControl _questionText;
            private readonly TextBox _textBox;
            private readonly ComboBox _comboBox;
            private readonly List<RadioButton> _radioButtons;
            private readonly RadioButton _otherRadioButton;
            private bool _inputEnabled = true;

            private bool HasChoices => _radioButtons != null || _comboBox != null;
            private bool IsOtherSelected => _otherRadioButton != null ? _otherRadioButton.Checked :
                _comboBox != null && _comboBox.SelectedIndex == _comboBox.Items.Count - 1;

            internal QuestionEditor(AppServerToolUserInputQuestion question, int questionNumber, int tabIndex, Action changed)
            {
                if (question == null)
                {
                    throw new ArgumentNullException(nameof(question));
                }

                QuestionId = question.Id;
                var displayedQuestion = questionNumber.ToString(CultureInfo.CurrentCulture) + ". " + (question.Question ?? string.Empty);
                Container = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    ColumnCount = 1,
                    RowCount = 2,
                    Margin = new Padding(0, 0, 0, UiTheme.ControlSpacing * 2),
                    TabStop = false
                };
                Container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                Container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Container.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                _questionText = new AccessibleStaticTextControl
                {
                    Dock = DockStyle.Fill,
                    Text = displayedQuestion,
                    AccessibleName = displayedQuestion,
                    AutoSize = true,
                    MinimumSize = new Size(0, 52),
                    BackColor = UiTheme.WindowBackColor,
                    ForeColor = UiTheme.TextColor,
                    TabIndex = tabIndex,
                    Margin = new Padding(0, 0, 0, UiTheme.ControlSpacing)
                };
                Container.Controls.Add(_questionText, 0, 0);

                var hasChoices = question.Options.Count > 0;
                _textBox = new ThemedTextBox
                {
                    Dock = DockStyle.Top,
                    Multiline = true,
                    AcceptsReturn = true,
                    ScrollBars = ScrollBars.Vertical,
                    Height = 84,
                    AccessibleName = hasChoices
                        ? UiLocalization.Text("AsyncInputForm.Accessible.OtherAnswer", "Other answer")
                        : BuildAnswerAccessibleName(),
                    TabIndex = tabIndex + (hasChoices ? 2 : 1)
                };
                UiTheme.ApplyTextBox(_textBox, false, false);
                _textBox.TextChanged += delegate { changed(); };
                Action selectionChanged = () => { UpdateOtherInput(); changed(); };
                var otherLabel = UiLocalization.Text("AsyncInputForm.Option.Other", "Other (enter your own answer)");
                if (!hasChoices)
                {
                    Container.Controls.Add(_textBox, 0, 1);
                }
                else if (question.Options.Count <= RadioOptionLimit)
                {
                    _radioButtons = new List<RadioButton>();
                    var options = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Top,
                        AutoSize = true,
                        FlowDirection = FlowDirection.TopDown,
                        WrapContents = false,
                        TabIndex = tabIndex + 1,
                        TabStop = false
                    };
                    foreach (var option in question.Options)
                    {
                        var radio = new RadioButton
                        {
                            AutoSize = true,
                            Text = option.Label,
                            AccessibleName = option.Label,
                            TabIndex = tabIndex + 1 + _radioButtons.Count,
                            TabStop = _radioButtons.Count == 0,
                            Tag = string.IsNullOrWhiteSpace(option.Value) ? option.Label : option.Value
                        };
                        radio.CheckedChanged += delegate { selectionChanged(); };
                        UiTheme.ApplyRadioButton(radio);
                        _radioButtons.Add(radio);
                        options.Controls.Add(radio);
                    }

                    _otherRadioButton = new RadioButton
                    {
                        AutoSize = true,
                        Text = otherLabel,
                        AccessibleName = otherLabel,
                        TabIndex = tabIndex + 1 + _radioButtons.Count,
                        TabStop = false
                    };
                    _otherRadioButton.CheckedChanged += delegate { selectionChanged(); };
                    UiTheme.ApplyRadioButton(_otherRadioButton);
                    _radioButtons.Add(_otherRadioButton);
                    options.Controls.Add(_otherRadioButton);

                    Container.Controls.Add(options, 0, 1);
                }
                else
                {
                    _comboBox = new ComboBox
                    {
                        Dock = DockStyle.Top,
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        AccessibleName = BuildAnswerAccessibleName(),
                        TabIndex = tabIndex + 1
                    };
                    foreach (var option in question.Options)
                    {
                        _comboBox.Items.Add(string.IsNullOrWhiteSpace(option.Value) ? option.Label : option.Value);
                    }

                    _comboBox.Items.Add(otherLabel);
                    _comboBox.SelectedIndexChanged += delegate { selectionChanged(); };
                    UiTheme.ApplyComboBox(_comboBox);
                    Container.Controls.Add(_comboBox, 0, 1);
                }

                if (hasChoices)
                {
                    Container.RowCount = 3;
                    Container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    Container.Controls.Add(_textBox, 0, 2);
                    UpdateOtherInput();
                }
            }

            internal string QuestionId { get; }

            internal TableLayoutPanel Container { get; }

            internal string Answer
            {
                get
                {
                    if (!HasChoices || IsOtherSelected)
                    {
                        return _textBox.Text;
                    }

                    if (_comboBox != null)
                    {
                        return _comboBox.SelectedItem as string;
                    }

                    var selected = _radioButtons != null ? _radioButtons.FirstOrDefault(button => button.Checked) : null;
                    return selected != null ? Convert.ToString(selected.Tag) : null;
                }
                set
                {
                    if (!HasChoices)
                    {
                        _textBox.Text = value ?? string.Empty;
                        return;
                    }

                    if (_comboBox != null)
                    {
                        var index = _comboBox.Items.IndexOf(value ?? string.Empty);
                        _comboBox.SelectedIndex = index < _comboBox.Items.Count - 1 ? index : -1;
                        if (_comboBox.SelectedIndex < 0 && !string.IsNullOrWhiteSpace(value))
                        {
                            _textBox.Text = value;
                            _comboBox.SelectedIndex = _comboBox.Items.Count - 1;
                        }
                        return;
                    }

                    foreach (var radio in _radioButtons ?? Enumerable.Empty<RadioButton>())
                    {
                        radio.Checked = radio != _otherRadioButton &&
                            string.Equals(Convert.ToString(radio.Tag), value, StringComparison.Ordinal);
                    }
                    if (!_radioButtons.Any(radio => radio.Checked) && !string.IsNullOrWhiteSpace(value))
                    {
                        _textBox.Text = value;
                        _otherRadioButton.Checked = true;
                    }
                }
            }

            internal bool RequiresOtherAnswer => IsOtherSelected && string.IsNullOrWhiteSpace(_textBox.Text);

            internal AsyncInputOtherAnswerDraft OtherDraft
            {
                get => !HasChoices ? null : new AsyncInputOtherAnswerDraft
                {
                    Text = _textBox.Text,
                    IsSelected = IsOtherSelected
                };
                set
                {
                    if (value == null || !HasChoices) return;
                    _textBox.Text = value.Text ?? string.Empty;
                    if (value.IsSelected)
                    {
                        if (_otherRadioButton != null) _otherRadioButton.Checked = true;
                        else _comboBox.SelectedIndex = _comboBox.Items.Count - 1;
                    }
                }
            }

            internal void FocusAnswer()
            {
                _textBox.Focus();
            }

            internal void FocusQuestion()
            {
                _questionText.Focus();
            }

            internal void SetInputEnabled(bool enabled)
            {
                _inputEnabled = enabled;
                _textBox.Enabled = enabled && (!HasChoices || IsOtherSelected);

                if (_comboBox != null)
                {
                    _comboBox.Enabled = enabled;
                }

                foreach (var radio in _radioButtons ?? Enumerable.Empty<RadioButton>())
                {
                    radio.Enabled = enabled;
                }
            }

            private void UpdateOtherInput()
            {
                _textBox.Visible = IsOtherSelected;
                _textBox.Enabled = _inputEnabled && IsOtherSelected;
            }

            private static string BuildAnswerAccessibleName()
            {
                return UiLocalization.Text("AsyncInputForm.Accessible.Answer", "Answer");
            }
        }
    }
}
