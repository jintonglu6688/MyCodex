using System;
using System.Collections.Generic;
using System.Drawing;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class McpToolTestForm : Form
    {
        private readonly IAppServerMcpClient _appServerClient;
        private readonly TextBox _threadIdTextBox;
        private readonly TextBox _toolTextBox;
        private readonly TextBox _argumentsTextBox;
        private readonly TextBox _metaTextBox;
        private readonly TextBox _resultTextBox;
        private readonly Label _statusLabel;
        private readonly Button _runButton;
        private readonly string _serverName;

        public McpToolTestForm(IAppServerMcpClient appServerClient, string threadId, string serverName)
        {
            _appServerClient = appServerClient ?? throw new ArgumentNullException(nameof(appServerClient));
            _serverName = serverName ?? string.Empty;

            Text = UiLocalization.Text("McpToolTestForm.Title", "MCP Tool Test");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 640);
            Size = new Size(920, 740);
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);

            _threadIdTextBox = CreateTextBox(UiLocalization.Text("McpToolTestForm.Accessible.Thread", "Thread"), 0, false);
            _threadIdTextBox.Text = threadId ?? string.Empty;
            _toolTextBox = CreateTextBox(UiLocalization.Text("McpToolTestForm.Accessible.Tool", "Tool"), 1, false);
            _argumentsTextBox = CreateTextBox(UiLocalization.Text("McpToolTestForm.Accessible.ArgumentsJson", "Arguments JSON"), 2, true);
            _argumentsTextBox.Text = "{}";
            _metaTextBox = CreateTextBox(UiLocalization.Text("McpToolTestForm.Accessible.MetadataJson", "Metadata JSON"), 3, true);
            _resultTextBox = CreateTextBox(UiLocalization.Text("McpToolTestForm.Accessible.Result", "Tool result"), 5, true);
            _resultTextBox.ReadOnly = true;

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = UiTheme.MutedTextColor,
                Text = UiLocalization.Text("McpToolTestForm.Status.Ready", "Ready."),
                TabStop = false
            };

            _runButton = new Button
            {
                Text = UiLocalization.Text("McpToolTestForm.Button.Run", "Run Tool(&R)"),
                AccessibleName = UiLocalization.Text("McpToolTestForm.Accessible.Run", "Run Tool"),
                AutoSize = true,
                TabIndex = 4
            };
            _runButton.Click += async (sender, e) => await RunToolAsync();
            UiTheme.ApplyDialogButton(_runButton, UiGlyph.Ok);

            BuildLayout();
        }

        internal void SetToolNameForTests(string toolName)
        {
            _toolTextBox.Text = toolName ?? string.Empty;
        }

        internal void SetArgumentsJsonForTests(string json)
        {
            _argumentsTextBox.Text = json ?? string.Empty;
        }

        internal string GetStatusTextForTests()
        {
            return _statusLabel.Text;
        }

        internal System.Threading.Tasks.Task RunToolForTests()
        {
            return RunToolAsync();
        }

        private async System.Threading.Tasks.Task RunToolAsync()
        {
            if (string.IsNullOrWhiteSpace(_threadIdTextBox.Text))
            {
                SetStatus(UiLocalization.Text("McpToolTestForm.Status.ThreadRequired", "Thread is required."));
                _threadIdTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(_toolTextBox.Text))
            {
                SetStatus(UiLocalization.Text("McpToolTestForm.Status.ToolRequired", "Tool is required."));
                _toolTextBox.Focus();
                return;
            }

            IDictionary<string, object> arguments;
            if (!TryParseJsonObject(_argumentsTextBox.Text, out arguments))
            {
                SetStatus(UiLocalization.Text("McpToolTestForm.Status.InvalidArgumentsJson", "Arguments JSON is invalid."));
                _argumentsTextBox.Focus();
                return;
            }

            IDictionary<string, object> meta;
            if (!TryParseJsonObject(_metaTextBox.Text, out meta))
            {
                SetStatus(UiLocalization.Text("McpToolTestForm.Status.InvalidMetadataJson", "Metadata JSON is invalid."));
                _metaTextBox.Focus();
                return;
            }

            try
            {
                _runButton.Enabled = false;
                SetStatus(UiLocalization.Text("McpToolTestForm.Status.Running", "Running tool..."));
                var result = await _appServerClient.CallMcpToolAsync(new AppServerMcpToolCallRequest
                {
                    ThreadId = _threadIdTextBox.Text.Trim(),
                    Server = _serverName,
                    Tool = _toolTextBox.Text.Trim(),
                    Arguments = arguments,
                    Meta = meta
                });
                _resultTextBox.Text = result != null ? (result.DisplayText ?? string.Empty) : string.Empty;
                SetStatus(UiLocalization.Text("McpToolTestForm.Status.Completed", "Tool call completed."));
            }
            catch (Exception ex)
            {
                _resultTextBox.Text = ex.Message;
                SetStatus(UiLocalization.Text("McpToolTestForm.Status.Failed", "Tool call failed."));
            }
            finally
            {
                _runButton.Enabled = true;
            }
        }

        private static bool TryParseJsonObject(string json, out IDictionary<string, object> result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return true;
            }

            try
            {
                var serializer = new JavaScriptSerializer();
                result = serializer.DeserializeObject(json) as IDictionary<string, object>;
                return result != null;
            }
            catch
            {
                return false;
            }
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text ?? string.Empty;
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 22F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(CreateLabeledControl(UiLocalization.Text("McpToolTestForm.Label.Thread", "Thread"), _threadIdTextBox), 0, 0);
            root.Controls.Add(CreateLabeledControl(UiLocalization.Text("McpToolTestForm.Label.Tool", "Tool"), _toolTextBox), 0, 1);
            root.Controls.Add(CreateLabeledControl(UiLocalization.Text("McpToolTestForm.Label.ArgumentsJson", "Arguments JSON"), _argumentsTextBox), 0, 2);
            root.Controls.Add(CreateLabeledControl(UiLocalization.Text("McpToolTestForm.Label.MetadataJson", "Metadata JSON"), _metaTextBox), 0, 3);
            root.Controls.Add(_statusLabel, 0, 4);
            root.Controls.Add(CreateLabeledControl(UiLocalization.Text("McpToolTestForm.Label.Result", "Tool result"), _resultTextBox), 0, 5);
            root.Controls.Add(CreateButtonsPanel(), 0, 6);

            Controls.Add(root);
        }

        private Control CreateButtonsPanel()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 0),
                TabStop = false
            };
            panel.Controls.Add(_runButton);
            return panel;
        }

        private static Control CreateLabeledControl(string labelText, Control control)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8),
                TabStop = false
            };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var label = new Label
            {
                Text = labelText,
                Dock = DockStyle.Fill,
                AutoSize = true,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            panel.Controls.Add(label, 0, 0);
            panel.Controls.Add(control, 0, 1);
            return panel;
        }

        private static TextBox CreateTextBox(string accessibleName, int tabIndex, bool multiline)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                TabIndex = tabIndex,
                Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Both : ScrollBars.None,
                WordWrap = !multiline,
                Font = multiline ? UiTheme.CreateCodeFont(UiTheme.ReadingFontSize, FontStyle.Regular) : UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular)
            };
            UiTheme.ApplyTextBox(textBox, multiline, textBox.ReadOnly);
            return textBox;
        }
    }
}
