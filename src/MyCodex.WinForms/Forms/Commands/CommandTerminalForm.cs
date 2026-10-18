using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class CommandTerminalForm : Form
    {
        private readonly Label _statusValueLabel;
        private readonly Label _commandValueLabel;
        private readonly Label _directoryValueLabel;
        private readonly CommandTerminalOutputControl _outputControl;
        private readonly TextBox _inputTextBox;
        private readonly Button _sendButton;
        private readonly Button _presetButton;
        private readonly Button _stopButton;
        private readonly Button _closeButton;
        private readonly CommandTerminalBuffer _buffer;
        private bool _canStartCommand;
        private bool _canSubmitInput;

        public CommandTerminalForm(CommandTask commandTask, CommandTerminalBuffer buffer, bool canSubmitInput, bool canStopCommand)
            : this(commandTask, buffer, false, canSubmitInput, canStopCommand)
        {
        }

        public CommandTerminalForm(CommandTask commandTask, CommandTerminalBuffer buffer, bool canStartCommand, bool canSubmitInput, bool canStopCommand)
        {
            _buffer = buffer ?? new CommandTerminalBuffer(131072);

            Text = UiLocalization.Text("CommandTerminalForm.Title", "Command Terminal");
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(780, 520);
            Size = new Size(960, 640);
            ShowInTaskbar = true;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var header = CreateHeaderPanel();
            _statusValueLabel = CreateValueLabel();
            _commandValueLabel = CreateValueLabel();
            _directoryValueLabel = CreateValueLabel();
            AddHeaderRow(header, 0, UiLocalization.Text("CommandTerminalForm.Label.Status", "Status"), _statusValueLabel);
            AddHeaderRow(header, 1, UiLocalization.Text("CommandTerminalForm.Label.Command", "Command"), _commandValueLabel);
            AddHeaderRow(header, 2, UiLocalization.Text("CommandTerminalForm.Label.Directory", "Directory"), _directoryValueLabel);

            _outputControl = new CommandTerminalOutputControl
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 10)
            };
            _outputControl.Resize += OutputControl_Resize;

            var inputPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                Height = 34,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var inputLabel = new Label
            {
                Text = UiLocalization.Text("CommandTerminalForm.Label.Input", "Input"),
                TextAlign = ContentAlignment.MiddleLeft,
                Width = 48,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 0),
                TabStop = false
            };
            UiTheme.ApplyLabel(inputLabel, true);

            _inputTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("CommandTerminalForm.Accessible.Input", "Input"),
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_inputTextBox, true, false);
            _inputTextBox.KeyDown += InputTextBox_KeyDown;

            _sendButton = CreateButton("CommandTerminalForm.Button.Send", "Send(&S)", "CommandTerminalForm.Accessible.Send", "Send", 1);
            UiTheme.ApplyButton(_sendButton, UiGlyph.Send);
            _sendButton.Click += SendButton_Click;

            _presetButton = CreateButton("CommandTerminalForm.Button.Presets", "Presets(&P)", "CommandTerminalForm.Accessible.Presets", "Presets", 2);
            UiTheme.ApplyButton(_presetButton, UiGlyph.Command);
            _presetButton.Click += PresetButton_Click;

            _stopButton = CreateButton("CommandTerminalForm.Button.Stop", "Stop(&T)", "CommandTerminalForm.Accessible.Stop", "Stop", 3);
            UiTheme.ApplyButton(_stopButton, UiGlyph.Stop);
            _stopButton.Click += StopButton_Click;

            _closeButton = CreateButton("CommandTerminalForm.Button.Close", "Close(&C)", "CommandTerminalForm.Accessible.Close", "Close", 4);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            _closeButton.Click += CloseButton_Click;

            inputPanel.Controls.Add(inputLabel, 0, 0);
            inputPanel.Controls.Add(_inputTextBox, 1, 0);
            inputPanel.Controls.Add(_sendButton, 2, 0);
            inputPanel.Controls.Add(_presetButton, 3, 0);
            inputPanel.Controls.Add(_stopButton, 4, 0);
            inputPanel.Controls.Add(_closeButton, 5, 0);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(_outputControl, 0, 1);
            root.Controls.Add(inputPanel, 0, 2);
            Controls.Add(root);

            CancelButton = _closeButton;
            UpdateCommand(commandTask, canStartCommand, canSubmitInput, canStopCommand);
        }

        public event EventHandler<CommandTerminalCommandSubmittedEventArgs> CommandSubmitted;

        public event EventHandler<CommandTerminalInputSubmittedEventArgs> InputSubmitted;

        public event EventHandler StopRequested;

        public event EventHandler PresetsRequested;

        public event EventHandler<CommandTerminalHotkeyEventArgs> ScreenReaderHotkeyPressed;

        public event EventHandler<CommandTerminalResizeRequestedEventArgs> TerminalResizeRequested;

        public void UpdateCommand(CommandTask commandTask, bool canSubmitInput, bool canStopCommand)
        {
            UpdateCommand(commandTask, false, canSubmitInput, canStopCommand);
        }

        public void UpdateCommand(CommandTask commandTask, bool canStartCommand, bool canSubmitInput, bool canStopCommand)
        {
            _canStartCommand = canStartCommand;
            _canSubmitInput = canSubmitInput;
            _statusValueLabel.Text = GetStatusText(commandTask);
            _commandValueLabel.Text = commandTask != null ? commandTask.CommandText ?? string.Empty : string.Empty;
            _directoryValueLabel.Text = commandTask != null ? commandTask.WorkingDirectory ?? string.Empty : string.Empty;

            var canSubmit = canStartCommand ||
                (canSubmitInput && commandTask != null && !string.IsNullOrWhiteSpace(commandTask.ProcessId));
            _inputTextBox.Enabled = canSubmit;
            _sendButton.Enabled = canSubmit;
            _stopButton.Enabled = canStopCommand;
            RefreshOutput();
        }

        public void AppendEntry(CommandTerminalEntryKind kind, string text)
        {
            _buffer.Append(kind, text);
            RefreshOutput();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _inputTextBox.Focus();
            _inputTextBox.SelectAll();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (TryForwardScreenReaderHotkey(keyData))
            {
                return true;
            }

            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        internal bool ProcessTerminalShortcutForTesting(Keys keyData)
        {
            return TryForwardScreenReaderHotkey(keyData);
        }

        internal AppServerCommandTerminalSize GetTerminalSizeForTests()
        {
            return CalculateTerminalSize();
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            RaiseTerminalResizeRequested();
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            SubmitInput();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        private void SendButton_Click(object sender, EventArgs e)
        {
            SubmitInput();
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            StopRequested?.Invoke(this, EventArgs.Empty);
        }

        private void PresetButton_Click(object sender, EventArgs e)
        {
            PresetsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void OutputControl_Resize(object sender, EventArgs e)
        {
            RaiseTerminalResizeRequested();
        }

        public void SetInputText(string text)
        {
            _inputTextBox.Text = text ?? string.Empty;
            _inputTextBox.Focus();
            _inputTextBox.SelectAll();
        }

        private void SubmitInput()
        {
            if (!_sendButton.Enabled)
            {
                return;
            }

            if (_canStartCommand && !_canSubmitInput)
            {
                var commandText = (_inputTextBox.Text ?? string.Empty).Trim();
                if (commandText.Length <= 0)
                {
                    return;
                }

                _inputTextBox.Clear();
                CommandSubmitted?.Invoke(this, new CommandTerminalCommandSubmittedEventArgs(commandText));
                return;
            }

            var inputText = (_inputTextBox.Text ?? string.Empty) + "\n";
            _inputTextBox.Clear();
            InputSubmitted?.Invoke(this, new CommandTerminalInputSubmittedEventArgs(inputText));
        }

        private bool TryForwardScreenReaderHotkey(Keys keyData)
        {
            if (!IsScreenReaderHotkey(keyData))
            {
                return false;
            }

            var args = new CommandTerminalHotkeyEventArgs(keyData);
            ScreenReaderHotkeyPressed?.Invoke(this, args);
            return args.Handled;
        }

        private static bool IsScreenReaderHotkey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Alt | Keys.Delete:
                case Keys.Alt | Keys.Up:
                case Keys.Alt | Keys.Down:
                case Keys.Alt | Keys.Left:
                case Keys.Alt | Keys.Right:
                case Keys.Alt | Keys.Home:
                case Keys.Alt | Keys.End:
                case Keys.Alt | Keys.PageUp:
                case Keys.Alt | Keys.PageDown:
                    return true;
                default:
                    return false;
            }
        }

        private void RefreshOutput()
        {
            _outputControl.SetEntries(_buffer.GetEntries());
        }

        private void RaiseTerminalResizeRequested()
        {
            TerminalResizeRequested?.Invoke(this, new CommandTerminalResizeRequestedEventArgs(CalculateTerminalSize()));
        }

        private AppServerCommandTerminalSize CalculateTerminalSize()
        {
            var clientSize = _outputControl.ClientSize;
            var font = _outputControl.Font ?? Font;
            var characterSize = TextRenderer.MeasureText("W", font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            var characterWidth = Math.Max(1, characterSize.Width);
            var lineHeight = Math.Max(1, font.Height);
            var cols = ClampTerminalCellCount(clientSize.Width / characterWidth);
            var rows = ClampTerminalCellCount(clientSize.Height / lineHeight);
            return new AppServerCommandTerminalSize(rows, cols);
        }

        private static ushort ClampTerminalCellCount(int value)
        {
            if (value < 1)
            {
                return 1;
            }

            if (value > ushort.MaxValue)
            {
                return ushort.MaxValue;
            }

            return (ushort)value;
        }

        private static TableLayoutPanel CreateHeaderPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return panel;
        }

        private static void AddHeaderRow(TableLayoutPanel panel, int row, string labelText, Label valueLabel)
        {
            var label = new Label
            {
                Text = labelText,
                Width = 64,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            panel.Controls.Add(label, 0, row);
            panel.Controls.Add(valueLabel, 1, row);
        }

        private static Label CreateValueLabel()
        {
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.TextColor,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private static Button CreateButton(string textKey, string textFallback, string accessibleKey, string accessibleFallback, int tabIndex)
        {
            return new Button
            {
                Text = UiLocalization.Text(textKey, textFallback),
                AccessibleName = UiLocalization.Text(accessibleKey, accessibleFallback),
                Width = 88,
                Height = 32,
                Margin = new Padding(8, 0, 0, 0),
                TabIndex = tabIndex,
                UseMnemonic = true
            };
        }

        private static string GetStatusText(CommandTask commandTask)
        {
            if (commandTask == null)
            {
                return UiLocalization.Text("CommandTerminalForm.Status.NoCommand", "No active command");
            }

            if (commandTask.IsWaitingForInput)
            {
                return UiLocalization.Text("CommandTerminalForm.Status.WaitingForInput", "Waiting for input");
            }

            switch ((commandTask.Status ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "completed":
                    return UiLocalization.Text("CommandTerminalForm.Status.Completed", "Completed");
                case "failed":
                    return UiLocalization.Text("CommandTerminalForm.Status.Failed", "Failed");
                case "terminated":
                    return UiLocalization.Text("CommandTerminalForm.Status.Stopped", "Stopped");
                case "running":
                    return UiLocalization.Text("CommandTerminalForm.Status.Running", "Running");
                default:
                    return commandTask.Status ?? string.Empty;
            }
        }
    }

    internal sealed class CommandTerminalInputSubmittedEventArgs : EventArgs
    {
        public CommandTerminalInputSubmittedEventArgs(string inputText)
        {
            InputText = inputText ?? string.Empty;
        }

        public string InputText { get; }
    }

    internal sealed class CommandTerminalCommandSubmittedEventArgs : EventArgs
    {
        public CommandTerminalCommandSubmittedEventArgs(string commandText)
        {
            CommandText = commandText ?? string.Empty;
        }

        public string CommandText { get; }
    }

    internal sealed class CommandTerminalHotkeyEventArgs : EventArgs
    {
        public CommandTerminalHotkeyEventArgs(Keys keyData)
        {
            KeyData = keyData;
        }

        public Keys KeyData { get; }

        public bool Handled { get; set; }
    }

    internal sealed class CommandTerminalResizeRequestedEventArgs : EventArgs
    {
        public CommandTerminalResizeRequestedEventArgs(AppServerCommandTerminalSize size)
        {
            Size = size;
        }

        public AppServerCommandTerminalSize Size { get; }
    }
}
