using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class McpDiagnosticsForm : Form
    {
        private readonly IAppServerMcpClient _appServerClient;
        private readonly AppServerMcpServerStatus _server;
        private readonly TextBox _detailsTextBox;
        private readonly Label _statusLabel;
        private readonly Button _readResourceButton;
        private readonly Button _testToolButton;

        public McpDiagnosticsForm(IAppServerMcpClient appServerClient, AppServerMcpServerStatus server)
        {
            _appServerClient = appServerClient ?? throw new ArgumentNullException(nameof(appServerClient));
            _server = server ?? throw new ArgumentNullException(nameof(server));

            Text = UiLocalization.Text("McpDiagnosticsForm.Title", "MCP Advanced Diagnostics");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(720, 520);
            Size = new Size(860, 640);
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);

            _detailsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = UiTheme.CreateCodeFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                AccessibleName = UiLocalization.Text("McpDiagnosticsForm.Accessible.Details", "Diagnostics details"),
                TabIndex = 0,
                Text = BuildDetailsText(_server)
            };
            UiTheme.ApplyTextBox(_detailsTextBox, true, true);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = UiTheme.MutedTextColor,
                Text = UiLocalization.Text("McpDiagnosticsForm.Status.Ready", "Ready."),
                TabStop = false
            };

            _readResourceButton = new Button
            {
                Text = UiLocalization.Text("McpDiagnosticsForm.Button.ReadResource", "Read Resource(&R)..."),
                AccessibleName = UiLocalization.Text("McpDiagnosticsForm.Accessible.ReadResource", "Read Resource"),
                AutoSize = true,
                TabIndex = 1
            };
            _readResourceButton.Click += async (sender, e) => await ReadResourceAsync();
            UiTheme.ApplyDialogButton(_readResourceButton, UiGlyph.Info);

            _testToolButton = new Button
            {
                Text = UiLocalization.Text("McpDiagnosticsForm.Button.TestTool", "Test Tool Call(&T)..."),
                AccessibleName = UiLocalization.Text("McpDiagnosticsForm.Accessible.TestTool", "Test Tool Call"),
                AutoSize = true,
                TabIndex = 2
            };
            _testToolButton.Click += (sender, e) => OpenToolTester();
            UiTheme.ApplyDialogButton(_testToolButton, UiGlyph.Info);

            BuildLayout();
        }

        private async System.Threading.Tasks.Task ReadResourceAsync()
        {
            using (var input = new ResourceUriInputForm(_server.Resources))
            {
                if (input.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(input.ResourceUri))
                {
                    return;
                }

                try
                {
                    SetBusy(true);
                    SetStatus(string.Format(
                        UiLocalization.Text("McpDiagnosticsForm.Status.ReadingResource", "Reading resource from {0}..."),
                        _server.ServerName));
                    var result = await _appServerClient.ReadMcpResourceAsync(_server.ServerName, input.ResourceUri, null);
                    using (var viewer = new McpResourceViewerForm(_server.ServerName, input.ResourceUri, result))
                    {
                        viewer.ShowDialog(this);
                    }

                    SetStatus(UiLocalization.Text("McpDiagnosticsForm.Status.ResourceRead", "Resource content loaded."));
                }
                catch (Exception ex)
                {
                    SetStatus(UiLocalization.Text("McpDiagnosticsForm.Status.ResourceReadFailed", "Failed to read MCP resource."));
                    MessageBox.Show(
                        this,
                        McpManagementForm.BuildResourceReadFailureMessage(_server.ServerName, input.ResourceUri, ex),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                finally
                {
                    SetBusy(false);
                }
            }
        }

        private void OpenToolTester()
        {
            using (var form = new McpToolTestForm(_appServerClient, null, _server.ServerName))
            {
                form.ShowDialog(this);
            }
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var headingLabel = new Label
            {
                Text = UiLocalization.Text(
                    "McpDiagnosticsForm.Heading",
                    "Advanced MCP diagnostics are intended for troubleshooting and protocol-level checks."),
                Dock = DockStyle.Fill,
                AutoSize = true,
                TabStop = false
            };
            UiTheme.ApplyLabel(headingLabel, false);

            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 0),
                TabStop = false
            };
            buttonsPanel.Controls.Add(_readResourceButton);
            buttonsPanel.Controls.Add(_testToolButton);

            root.Controls.Add(headingLabel, 0, 0);
            root.Controls.Add(_detailsTextBox, 0, 1);
            root.Controls.Add(_statusLabel, 0, 2);
            root.Controls.Add(buttonsPanel, 0, 3);
            Controls.Add(root);
        }

        private void SetBusy(bool isBusy)
        {
            UseWaitCursor = isBusy;
            _readResourceButton.Enabled = !isBusy;
            _testToolButton.Enabled = !isBusy;
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text ?? string.Empty;
        }

        private static string BuildDetailsText(AppServerMcpServerStatus server)
        {
            var builder = new StringBuilder();
            AppendLine(builder, UiLocalization.Text("McpDiagnosticsForm.Detail.Server", "Server"), server.ServerName);
            AppendLine(builder, UiLocalization.Text("McpDiagnosticsForm.Detail.Status", "Status"), server.Status);
            AppendLine(builder, UiLocalization.Text("McpDiagnosticsForm.Detail.Auth", "Auth"), server.AuthStatus);
            AppendLine(builder, UiLocalization.Text("McpDiagnosticsForm.Detail.Source", "Source"), server.SourceSummary);
            AppendLine(builder, UiLocalization.Text("McpDiagnosticsForm.Detail.Entry", "Entry"), server.SourceDetail);
            AppendLine(builder, UiLocalization.Text("McpDiagnosticsForm.Detail.Tools", "Tools"), FormatNullableCount(server.ToolCount));
            AppendLine(builder, UiLocalization.Text("McpDiagnosticsForm.Detail.Resources", "Resources"), FormatNullableCount(server.ResourceCount));
            AppendLine(builder, UiLocalization.Text("McpDiagnosticsForm.Detail.Templates", "Templates"), FormatNullableCount(server.ResourceTemplateCount));
            AppendLine(builder, UiLocalization.Text("McpDiagnosticsForm.Detail.Error", "Error"), server.ErrorText);
            if (!string.IsNullOrWhiteSpace(server.DetailText))
            {
                builder.AppendLine();
                builder.AppendLine(server.DetailText);
            }

            return builder.ToString().TrimEnd();
        }

        private static string FormatNullableCount(int? value)
        {
            return value.HasValue ? value.Value.ToString() : null;
        }

        private static void AppendLine(StringBuilder builder, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            builder.Append(label);
            builder.Append(": ");
            builder.AppendLine(value.Trim());
        }
    }
}
