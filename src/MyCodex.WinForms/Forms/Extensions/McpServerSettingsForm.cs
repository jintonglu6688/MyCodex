using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class McpServerSettingsForm : Form
    {
        private readonly AppServerMcpServerStatus _server;
        private readonly bool _isNewServer;
        private readonly Dictionary<string, object> _baseConfig;
        private readonly HashSet<string> _existingServerNames;
        private readonly TextBox _nameTextBox;
        private readonly CheckBox _enabledCheckBox;
        private readonly RadioButton _httpRadioButton;
        private readonly RadioButton _commandRadioButton;
        private readonly TextBox _urlTextBox;
        private readonly TextBox _commandTextBox;
        private readonly TextBox _argumentsTextBox;
        private readonly TextBox _workingDirectoryTextBox;
        private readonly TextBox _environmentTextBox;
        private readonly Button _saveButton;
        private readonly Button _cancelButton;

        public McpServerSettingsForm(AppServerMcpServerStatus server, IEnumerable<string> existingServerNames = null)
        {
            _server = server;
            _isNewServer = server == null;
            _baseConfig = CloneDictionary(server != null ? server.ConfigValues : null) ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _existingServerNames = new HashSet<string>(
                (existingServerNames ?? Enumerable.Empty<string>())
                    .Where(item => !string.IsNullOrWhiteSpace(item)),
                StringComparer.OrdinalIgnoreCase);
            if (!_isNewServer && !string.IsNullOrWhiteSpace(server.ServerName))
            {
                _existingServerNames.Remove(server.ServerName);
            }

            Text = _isNewServer
                ? UiLocalization.Text("McpServerSettingsForm.Title.New", "Add MCP Server")
                : UiLocalization.Text("McpServerSettingsForm.Title.Edit", "MCP Server Settings");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 620);
            Size = new Size(820, 700);
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);

            _nameTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = !_isNewServer,
                AccessibleName = UiLocalization.Text("McpServerSettingsForm.Accessible.Name", "Name"),
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_nameTextBox, false, false);
            _enabledCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("McpServerSettingsForm.Button.Enabled", "Enabled(&E)"),
                AccessibleName = UiLocalization.Text("McpServerSettingsForm.Accessible.Enabled", "Enabled"),
                AutoSize = true,
                UseMnemonic = true,
                TabIndex = 1
            };
            UiTheme.ApplyCheckBox(_enabledCheckBox);
            _httpRadioButton = new RadioButton
            {
                Text = "HTTP(&H)",
                AccessibleName = "HTTP",
                AutoSize = true,
                UseMnemonic = true,
                TabIndex = 2
            };
            UiTheme.ApplyRadioButton(_httpRadioButton);
            _commandRadioButton = new RadioButton
            {
                Text = UiLocalization.Text("McpServerSettingsForm.Button.TransportCommand", "Command(&M)"),
                AccessibleName = UiLocalization.Text("McpServerSettingsForm.Accessible.Command", "Command"),
                AutoSize = true,
                UseMnemonic = true,
                TabIndex = 3
            };
            UiTheme.ApplyRadioButton(_commandRadioButton);
            _urlTextBox = CreateTextBox(UiLocalization.Text("McpServerSettingsForm.Accessible.Url", "URL"), 4);
            _commandTextBox = CreateTextBox(UiLocalization.Text("McpServerSettingsForm.Accessible.Command", "Command"), 5);
            _argumentsTextBox = CreateMultiLineTextBox(UiLocalization.Text("McpServerSettingsForm.Accessible.Arguments", "Arguments"), 6, 88);
            _workingDirectoryTextBox = CreateTextBox(UiLocalization.Text("McpServerSettingsForm.Accessible.WorkingDirectory", "Working Directory"), 7);
            _environmentTextBox = CreateMultiLineTextBox(UiLocalization.Text("McpServerSettingsForm.Accessible.Environment", "Environment Variables"), 8, 128);
            _saveButton = CreateButton(
                UiLocalization.Text("McpServerSettingsForm.Button.Save", "Save(&S)"),
                UiLocalization.Text("McpServerSettingsForm.Accessible.Save", "Save"),
                9);
            _cancelButton = CreateButton(
                UiLocalization.Text("McpServerSettingsForm.Button.Cancel", "&Cancel"),
                UiLocalization.Text("McpServerSettingsForm.Accessible.Cancel", "Cancel"),
                10);
            UiTheme.ApplyButton(_saveButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _httpRadioButton.CheckedChanged += TransportRadioButton_CheckedChanged;
            _commandRadioButton.CheckedChanged += TransportRadioButton_CheckedChanged;
            _saveButton.Click += SaveButton_Click;
            _cancelButton.Click += CancelButton_Click;

            BuildLayout();
            LoadValues();

            AcceptButton = _saveButton;
            CancelButton = _cancelButton;
        }

        public Dictionary<string, object> EditedConfig { get; private set; }

        public string EditedServerName { get; private set; }

        private void LoadValues()
        {
            _nameTextBox.Text = _server != null ? (_server.ServerName ?? string.Empty) : string.Empty;
            _enabledCheckBox.Checked = JsonValueReader.GetNullableBool(_baseConfig, "enabled") ?? (_server != null ? _server.Enabled : null) ?? true;

            var url = JsonValueReader.GetFirstNonEmptyString(_baseConfig, "url", "baseUrl", "serverUrl", "endpoint");
            var command = JsonValueReader.GetString(_baseConfig, "command");
            var arguments = JoinArrayLines(JsonValueReader.GetValue(_baseConfig, "args"));
            var workingDirectory = JsonValueReader.GetString(_baseConfig, "cwd");
            var environment = BuildEnvironmentLines(JsonValueReader.GetValue(_baseConfig, "env")) ?? BuildEnvironmentLines(JsonValueReader.GetValue(_baseConfig, "env_vars"));

            _urlTextBox.Text = url ?? string.Empty;
            _commandTextBox.Text = command ?? string.Empty;
            _argumentsTextBox.Text = arguments ?? string.Empty;
            _workingDirectoryTextBox.Text = workingDirectory ?? string.Empty;
            _environmentTextBox.Text = environment ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(url))
            {
                _httpRadioButton.Checked = true;
            }
            else if (_isNewServer)
            {
                _commandRadioButton.Checked = true;
            }
            else
            {
                _commandRadioButton.Checked = true;
            }

            UpdateTransportEditors();
        }

        private void BuildLayout()
        {
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };

            var titleLabel = new Label
            {
                Text = _isNewServer
                    ? UiLocalization.Text("McpServerSettingsForm.Header.New", "Add an MCP server and configure its connection method.")
                    : UiLocalization.Text("McpServerSettingsForm.Header.Edit", "Adjust enabled state and connection method for one MCP server."),
                Dock = DockStyle.Top,
                Height = 34,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var tipLabel = new Label
            {
                Text = UiLocalization.Text(
                    "McpServerSettingsForm.Tip",
                    "Enter one argument per line. For environment variables, use KEY=VALUE and one entry per line."),
                Dock = DockStyle.Top,
                Height = 28,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(0, 12, 0, 0),
                TabStop = false
            };

            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 220,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            buttonsPanel.Controls.Add(_saveButton);
            buttonsPanel.Controls.Add(_cancelButton);
            footerPanel.Controls.Add(buttonsPanel);

            var contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
                Margin = new Padding(0),
                Padding = new Padding(0, 8, 0, 0),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var nameLabel = CreateLabel(UiLocalization.Text("McpServerSettingsForm.Label.Name", "Name"));
            var enabledLabel = CreateLabel(UiLocalization.Text("McpServerSettingsForm.Label.Enabled", "Enabled"));
            var transportLabel = CreateLabel(UiLocalization.Text("McpServerSettingsForm.Label.Transport", "Transport"));
            var urlLabel = CreateLabel(UiLocalization.Text("McpServerSettingsForm.Label.Url", "URL"));
            var commandLabel = CreateLabel(UiLocalization.Text("McpServerSettingsForm.Label.Command", "Command"));
            var argumentsLabel = CreateLabel(UiLocalization.Text("McpServerSettingsForm.Label.Arguments", "Arguments"));
            var workingDirectoryLabel = CreateLabel(UiLocalization.Text("McpServerSettingsForm.Label.WorkingDirectory", "Working Directory"));
            var environmentLabel = CreateLabel(UiLocalization.Text("McpServerSettingsForm.Label.Environment", "Environment Variables"));

            var transportPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 3),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            transportPanel.Controls.Add(_httpRadioButton);
            transportPanel.Controls.Add(_commandRadioButton);

            contentLayout.Controls.Add(nameLabel, 0, 0);
            contentLayout.Controls.Add(_nameTextBox, 1, 0);
            contentLayout.Controls.Add(enabledLabel, 0, 1);
            contentLayout.Controls.Add(_enabledCheckBox, 1, 1);
            contentLayout.Controls.Add(transportLabel, 0, 2);
            contentLayout.Controls.Add(transportPanel, 1, 2);
            contentLayout.Controls.Add(urlLabel, 0, 3);
            contentLayout.Controls.Add(_urlTextBox, 1, 3);
            contentLayout.Controls.Add(commandLabel, 0, 4);
            contentLayout.Controls.Add(_commandTextBox, 1, 4);
            contentLayout.Controls.Add(argumentsLabel, 0, 5);
            contentLayout.Controls.Add(_argumentsTextBox, 1, 5);
            contentLayout.Controls.Add(workingDirectoryLabel, 0, 6);
            contentLayout.Controls.Add(_workingDirectoryTextBox, 1, 6);
            contentLayout.Controls.Add(environmentLabel, 0, 7);
            contentLayout.Controls.Add(_environmentTextBox, 1, 7);

            mainPanel.Controls.Add(contentLayout);
            mainPanel.Controls.Add(footerPanel);
            mainPanel.Controls.Add(tipLabel);
            mainPanel.Controls.Add(titleLabel);

            Controls.Add(mainPanel);
        }

        private void TransportRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            UpdateTransportEditors();
        }

        private void UpdateTransportEditors()
        {
            var useHttp = _httpRadioButton.Checked;
            _urlTextBox.Enabled = useHttp;
            _commandTextBox.Enabled = !useHttp;
            _argumentsTextBox.Enabled = !useHttp;
            _workingDirectoryTextBox.Enabled = !useHttp;
            _environmentTextBox.Enabled = !useHttp;
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            string serverName;
            string nameErrorText;
            if (!TryGetValidatedServerName(out serverName, out nameErrorText))
            {
                MessageBox.Show(this, nameErrorText, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _nameTextBox.Focus();
                return;
            }

            var config = CloneDictionary(_baseConfig) ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            config["enabled"] = _enabledCheckBox.Checked;

            if (_httpRadioButton.Checked)
            {
                var url = (_urlTextBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(url))
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("McpServerSettingsForm.Message.HttpUrlRequired", "URL is required in HTTP mode."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    _urlTextBox.Focus();
                    return;
                }

                config["url"] = url;
                if (config.ContainsKey("type") && JsonValueReader.GetString(config, "type") != "sse") config["type"] = "http";
                RemoveKeys(config, "command", "args", "cwd", "env", "env_vars", "baseUrl", "serverUrl", "endpoint");
            }
            else
            {
                var command = (_commandTextBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(command))
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("McpServerSettingsForm.Message.CommandRequired", "Command is required in command mode."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    _commandTextBox.Focus();
                    return;
                }

                config["command"] = command;
                if (config.ContainsKey("type")) config["type"] = "stdio";
                SetOrRemove(config, "cwd", (_workingDirectoryTextBox.Text ?? string.Empty).Trim());

                var args = ParseArguments(_argumentsTextBox.Text);
                if (args.Length > 0)
                {
                    config["args"] = args;
                }
                else
                {
                    config.Remove("args");
                }

                Dictionary<string, object> envValues;
                string envError;
                if (!TryParseEnvironment(_environmentTextBox.Text, out envValues, out envError))
                {
                    MessageBox.Show(this, envError, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _environmentTextBox.Focus();
                    return;
                }

                if (envValues != null && envValues.Count > 0)
                {
                    if (config.ContainsKey("env_vars") && !config.ContainsKey("env"))
                    {
                        config["env_vars"] = envValues;
                    }
                    else
                    {
                        config["env"] = envValues;
                        config.Remove("env_vars");
                    }
                }
                else
                {
                    config.Remove("env");
                    config.Remove("env_vars");
                }

                RemoveKeys(config, "url", "baseUrl", "serverUrl", "endpoint");
            }

            EditedServerName = serverName;
            EditedConfig = config;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private bool TryGetValidatedServerName(out string serverName, out string errorText)
        {
            serverName = (_nameTextBox.Text ?? string.Empty).Trim();
            errorText = null;

            if (serverName.Length == 0)
            {
                errorText = UiLocalization.Text("McpServerSettingsForm.Error.NameRequired", "Please enter a server name.");
                return false;
            }

            if (!_isNewServer &&
                string.Equals(serverName, _server != null ? _server.ServerName : null, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var ch in serverName)
            {
                if ((ch >= 'a' && ch <= 'z') ||
                    (ch >= 'A' && ch <= 'Z') ||
                    (ch >= '0' && ch <= '9') ||
                    ch == '-' ||
                    ch == '_')
                {
                    continue;
                }

                errorText = UiLocalization.Text("McpServerSettingsForm.Error.NameInvalidChars", "Server name can only contain letters, digits, hyphens, and underscores.");
                return false;
            }

            if (_existingServerNames.Contains(serverName))
            {
                errorText = UiLocalization.Text("McpServerSettingsForm.Error.NameAlreadyExists", "This server name already exists. Please choose another one.");
                return false;
            }

            return true;
        }

        private static Label CreateLabel(string text)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(0, 8, 12, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private static TextBox CreateTextBox(string accessibleName, int tabIndex)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                Margin = new Padding(0, 4, 0, 4),
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, false, false);
            return textBox;
        }

        private static TextBox CreateMultiLineTextBox(string accessibleName, int tabIndex, int height)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                AccessibleName = accessibleName,
                Height = height,
                Margin = new Padding(0, 4, 0, 4),
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, true, false);
            return textBox;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 96,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }

        private static string[] ParseArguments(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new string[0];
            }

            return text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
        }

        private static bool TryParseEnvironment(string text, out Dictionary<string, object> envValues, out string errorText)
        {
            envValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            errorText = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = (lines[i] ?? string.Empty).Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var delimiterIndex = line.IndexOf('=');
                if (delimiterIndex <= 0)
                {
                    errorText = string.Format(
                        UiLocalization.Text("McpServerSettingsForm.Error.EnvironmentInvalidLine", "Environment variable line {0} has invalid format. Use KEY=VALUE."),
                        i + 1);
                    envValues = null;
                    return false;
                }

                var key = line.Substring(0, delimiterIndex).Trim();
                if (key.Length == 0)
                {
                    errorText = string.Format(
                        UiLocalization.Text("McpServerSettingsForm.Error.EnvironmentMissingName", "Environment variable line {0} is missing a name."),
                        i + 1);
                    envValues = null;
                    return false;
                }

                envValues[key] = line.Substring(delimiterIndex + 1);
            }

            return true;
        }

        private static string JoinArrayLines(object rawValue)
        {
            var lines = new List<string>();
            var enumerable = rawValue as IEnumerable;
            if (enumerable == null || rawValue is string)
            {
                return null;
            }

            foreach (var item in enumerable)
            {
                var text = item == null ? null : Convert.ToString(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(text.Trim());
                }
            }

            return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines.ToArray());
        }

        private static string BuildEnvironmentLines(object rawValue)
        {
            var dictionary = rawValue as IDictionary<string, object>;
            if (dictionary == null || dictionary.Count == 0)
            {
                return null;
            }

            var lines = new List<string>();
            foreach (var pair in dictionary.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(string.Format("{0}={1}", pair.Key, pair.Value == null ? string.Empty : Convert.ToString(pair.Value)));
            }

            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private static Dictionary<string, object> CloneDictionary(IDictionary<string, object> source)
        {
            if (source == null)
            {
                return null;
            }

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in source)
            {
                result[pair.Key] = CloneValue(pair.Value);
            }

            return result;
        }

        private static object CloneValue(object value)
        {
            var dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                return CloneDictionary(dictionary);
            }

            if (value == null || value is string)
            {
                return value;
            }

            var enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                var items = new List<object>();
                foreach (var item in enumerable)
                {
                    items.Add(CloneValue(item));
                }

                return items.ToArray();
            }

            return value;
        }

        private static void RemoveKeys(IDictionary<string, object> target, params string[] keys)
        {
            if (target == null || keys == null)
            {
                return;
            }

            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    target.Remove(key);
                }
            }
        }

        private static void SetOrRemove(IDictionary<string, object> target, string key, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                target.Remove(key);
                return;
            }

            target[key] = value;
        }
    }
}
