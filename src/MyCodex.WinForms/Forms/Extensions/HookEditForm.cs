using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Localization;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    /// <summary>Collects one new command or MCP tool hook for the user or project hooks.json.</summary>
    public sealed class HookEditForm : Form
    {
        private readonly ComboBox _locationComboBox;
        private readonly ComboBox _eventComboBox;
        private readonly TextBox _matcherTextBox;
        private readonly ComboBox _typeComboBox;
        private readonly TextBox _commandTextBox;
        private readonly TextBox _serverTextBox;
        private readonly TextBox _toolTextBox;
        private readonly TextBox _timeoutTextBox;
        private readonly TextBox _statusMessageTextBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly TableLayoutPanel _fields;

        public HookEditForm(bool canUseProject)
        {
            Text = UiLocalization.Text("HookEditForm.Title", "New Hook");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 460);
            Size = new Size(640, 500);
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);

            _locationComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("HookEditForm.Accessible.Location", "Location"), 1, DockStyle.Fill);
            _locationComboBox.Items.Add(UiLocalization.Text("HookEditForm.Location.User", "User hooks.json"));
            if (canUseProject)
            {
                _locationComboBox.Items.Add(UiLocalization.Text("HookEditForm.Location.Project", "Project hooks.json"));
            }

            _locationComboBox.SelectedIndex = 0;
            _eventComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("HookEditForm.Accessible.Event", "Event"), 3, DockStyle.Fill);
            _eventComboBox.Items.AddRange(HookSourceFileEditor.EventNames
                .Select(name => (object)HookEventNames.DescribeWithOfficialName(name)).ToArray());
            _eventComboBox.SelectedIndex = 0;
            _matcherTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("HookEditForm.Accessible.Matcher", "Matcher"), 5);
            _typeComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("HookEditForm.Accessible.Type", "Handler type"), 7, DockStyle.Fill);
            _typeComboBox.Items.Add(UiLocalization.Text("HookEditForm.Type.Command", "Command"));
            _typeComboBox.Items.Add(UiLocalization.Text("HookEditForm.Type.McpTool", "MCP tool"));
            _typeComboBox.SelectedIndex = 0;
            _commandTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("HookEditForm.Accessible.Command", "Command"), 9);
            _serverTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("HookEditForm.Accessible.Server", "MCP server"), 11);
            _toolTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("HookEditForm.Accessible.Tool", "MCP tool"), 13);
            _timeoutTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("HookEditForm.Accessible.Timeout", "Timeout in seconds"), 15);
            _statusMessageTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("HookEditForm.Accessible.StatusMessage", "Status message"), 17);

            _okButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("Common.OK", "&OK"),
                UiLocalization.Text("Common.Accessible.OK", "OK"),
                18,
                UiGlyph.Ok);
            _cancelButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("Common.Cancel", "&Cancel"),
                UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                19,
                UiGlyph.Cancel,
                DialogResult.Cancel);

            _fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            _fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            AddRow(UiLocalization.Text("HookEditForm.Label.Location", "&Location:"), _locationComboBox, 0);
            AddRow(UiLocalization.Text("HookEditForm.Label.Event", "&Event:"), _eventComboBox, 2);
            AddRow(UiLocalization.Text("HookEditForm.Label.Matcher", "&Matcher (optional):"), _matcherTextBox, 4);
            AddRow(UiLocalization.Text("HookEditForm.Label.Type", "T&ype:"), _typeComboBox, 6);
            AddRow(UiLocalization.Text("HookEditForm.Label.Command", "Comman&d:"), _commandTextBox, 8);
            AddRow(UiLocalization.Text("HookEditForm.Label.Server", "MCP &server:"), _serverTextBox, 10);
            AddRow(UiLocalization.Text("HookEditForm.Label.Tool", "MCP &tool:"), _toolTextBox, 12);
            AddRow(UiLocalization.Text("HookEditForm.Label.Timeout", "T&imeout in seconds (optional):"), _timeoutTextBox, 14);
            AddRow(UiLocalization.Text("HookEditForm.Label.StatusMessage", "Status messa&ge (optional):"), _statusMessageTextBox, 16);
            var buttons = UiControlFactory.CreateDialogButtonBar();
            buttons.Controls.AddRange(new Control[] { _cancelButton, _okButton });
            _fields.Controls.Add(buttons, 0, _fields.RowCount);
            _fields.SetColumnSpan(buttons, 2);
            Controls.Add(_fields);

            _typeComboBox.SelectedIndexChanged += (sender, e) => RefreshTypeFields();
            _okButton.Click += (sender, e) => Accept();
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            RefreshTypeFields();
        }

        public bool IsProjectLocation
        {
            get { return _locationComboBox.SelectedIndex == 1; }
        }

        public string EventName
        {
            get { return HookSourceFileEditor.EventNames[_eventComboBox.SelectedIndex]; }
        }

        public string Matcher
        {
            get { return _matcherTextBox.Text.Trim(); }
        }

        public Dictionary<string, object> Handler { get; private set; }

        private bool IsCommand
        {
            get { return _typeComboBox.SelectedIndex == 0; }
        }

        private void AddRow(string labelText, Control control, int labelTabIndex)
        {
            var label = new Label
            {
                Text = labelText,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                UseMnemonic = true,
                TabIndex = labelTabIndex
            };
            UiTheme.ApplyLabel(label, false);
            var row = _fields.RowCount++;
            _fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _fields.Controls.Add(label, 0, row);
            _fields.Controls.Add(control, 1, row);
        }

        private void RefreshTypeFields()
        {
            _commandTextBox.Enabled = IsCommand;
            _serverTextBox.Enabled = !IsCommand;
            _toolTextBox.Enabled = !IsCommand;
        }

        private void Accept()
        {
            int timeout = 0;
            var timeoutText = _timeoutTextBox.Text.Trim();
            var hasTimeout = timeoutText.Length > 0;
            string error = null;
            if (hasTimeout && (!int.TryParse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out timeout) || timeout <= 0))
            {
                error = UiLocalization.Text("HookEditForm.Error.Timeout", "Timeout must be a whole number of seconds greater than zero.");
            }
            else if (IsCommand && string.IsNullOrWhiteSpace(_commandTextBox.Text))
            {
                error = UiLocalization.Text("HookEditForm.Error.Command", "Enter the command to run.");
            }
            else if (!IsCommand && (string.IsNullOrWhiteSpace(_serverTextBox.Text) || string.IsNullOrWhiteSpace(_toolTextBox.Text)))
            {
                error = UiLocalization.Text("HookEditForm.Error.McpTool", "Enter both the MCP server and the tool.");
            }

            if (error != null)
            {
                MessageBox.Show(this, error, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int? timeoutSec = hasTimeout ? timeout : (int?)null;
            Handler = IsCommand
                ? HookSourceFileEditor.BuildCommandHandler(_commandTextBox.Text.Trim(), timeoutSec, _statusMessageTextBox.Text)
                : HookSourceFileEditor.BuildMcpToolHandler(_serverTextBox.Text.Trim(), _toolTextBox.Text.Trim(), timeoutSec, _statusMessageTextBox.Text);
            DialogResult = DialogResult.OK;
        }
    }
}
