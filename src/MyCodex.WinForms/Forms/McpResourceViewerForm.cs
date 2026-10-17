using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class McpResourceViewerForm : Form
    {
        private readonly TextBox _contentTextBox;

        public McpResourceViewerForm(string serverName, string uri, AppServerMcpResourceReadResult result)
        {
            Text = UiLocalization.Text("McpResourceViewerForm.Title", "MCP Resource");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(720, 520);
            Size = new Size(860, 640);
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);

            _contentTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = UiTheme.CreateCodeFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                AccessibleName = UiLocalization.Text("McpResourceViewerForm.Accessible.Content", "Resource content"),
                TabIndex = 0,
                Text = BuildDisplayText(serverName, uri, result)
            };
            UiTheme.ApplyTextBox(_contentTextBox, true, true);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(_contentTextBox, 0, 0);

            Controls.Add(root);
            CancelButton = null;
        }

        private static string BuildDisplayText(string serverName, string uri, AppServerMcpResourceReadResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Server: " + (serverName ?? string.Empty));
            builder.AppendLine("URI: " + (uri ?? string.Empty));
            builder.AppendLine();

            if (result == null || result.Contents.Count == 0)
            {
                builder.AppendLine(UiLocalization.Text("McpResourceViewerForm.Value.Empty", "No resource content was returned."));
                return builder.ToString();
            }

            for (var i = 0; i < result.Contents.Count; i++)
            {
                var content = result.Contents[i];
                builder.Append("[");
                builder.Append(i + 1);
                builder.Append("] ");
                builder.AppendLine(content.Uri ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(content.MimeType))
                {
                    builder.AppendLine("MIME: " + content.MimeType);
                }

                builder.AppendLine();
                if (content.IsBlob)
                {
                    builder.AppendFormat(
                        UiLocalization.Text("McpResourceViewerForm.Value.BlobSummary", "Blob content: {0} base64 characters"),
                        (content.Blob ?? string.Empty).Length);
                    builder.AppendLine();
                }
                else
                {
                    builder.AppendLine(content.Text ?? string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(content.MetaSummary))
                {
                    builder.AppendLine();
                    builder.AppendLine("_meta: " + content.MetaSummary);
                }

                if (i + 1 < result.Contents.Count)
                {
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }
    }
}
